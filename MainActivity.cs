using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Nfc;
using Android.OS;
using Android.Preferences;
using Android.Runtime;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.AppCompat.Widget;
using AndroidX.Core.App;
using AndroidX.Fragment.App;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Rfid;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using Java.Util;
using RFIDTrackBin.enums;
using RFIDTrackBin.fragment;
using RFIDTrackBin.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001; // Importa la Toolbar
using System.Text;
using System.Threading.Tasks;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;
using MySql.Data.MySqlClient;

namespace RFIDTrackBin
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", Exported = false)]
    [IntentFilter(new[] { NfcAdapter.ActionNdefDiscovered, NfcAdapter.ActionTagDiscovered, Intent.CategoryDefault })]
    public class MainActivity : AppCompatActivity, BottomNavigationView.IOnNavigationItemSelectedListener
    {
        #region CONEXION BASE DE DATOS MYSQL
        MySqlConnection mySqlConn = new MySqlConnection("server=gab.mrlucky.com.mx;port=3306;userid=www1166;password=taQ17Zm;database=campo");
        MySqlCommand cmnd = new MySqlCommand();
        MySqlCommand cmnd0 = new MySqlCommand();
        MySqlDataReader reader;
        #endregion

        public static string cadenaConexion = "Persist Security Info=False;user id=sa; password=Gabira1;Initial Catalog = GAB_Irapuato; server=tcp:189.206.160.206,2352; MultipleActiveResultSets=true; Connect Timeout = 0";
        public static string cadenaConexionMySQL = "server=gab.mrlucky.com.mx;port=3306;database=campo;user id=www1166;password=taQ17Zm;";
        private const string TAG = nameof(MainActivity);
        private const int REQUEST_PERMISSION_CODE = 1000;

        private static MainActivity instance;
        private static MainHandler _handler;

        private TextView textMessage;
        //public BaseReader baseReader;
        public MainModel mainModel;

        public static MainActivity Instance => instance;

        #region BASE DE DATOS
        DataSet ds = new DataSet();
        #region TABLAS
        public DataTable Tb_RFID_Catalogo = new DataTable("Tb_RFID_Catalogo");
        #endregion
        #endregion

        #region NFC
        NfcAdapter nfcAdapter;
        NfcAdapter _nfcAdapter;
        PendingIntent nfcPendingIntent;
        IntentFilter[] nfcIntentFilters;
        string[][] techLists;

        #endregion

        #region VARIABLES HEREDADAS INTENT
        public string usuario = "";
        #endregion

        #region RFID
        public BaseReader baseReader { get; private set; }
        public bool IsReaderConnected { get; private set; }
        Bundle tempKeyCode = null;
        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";
        #endregion

        #region BOTON FLOTANTE PARA DAR DE BAJA CAJONES
        private FloatingActionButton fabMain;
        private float dX, dY;
        private int lastAction;
        private int screenWidth, screenHeight;
        #endregion

        #region VARIABLES PARA BAJA DE CAJONES
        private GridView gvQR;
        private List<string> qrList = new List<string>();
        private myGVitemAdapter qrAdapter;
        private Android.App.AlertDialog bajaDialog;
        #endregion

        public BaseFragment currentRfidFragment { get; set; }

        public BottomNavigationView BottomNavigation { get; private set; } // Propiedad para BottomNavigationView

        public static BluetoothHelper BtHelper { get; private set; }

        public HoraServidorService.ResultadoHora Privilegios { get; set; }


        protected override async void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Habilitar soporte para Windows-1252
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            #region APPLOGGER
            // Captura errores no manejados
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLogger.LogError((Exception)e.ExceptionObject);
                //RemoteLogger.SendError((Exception)e.ExceptionObject); // lo veremos abajo
            };

            AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                AppLogger.LogError(e.Exception);
                //RemoteLogger.SendError(e.Exception); // lo veremos abajo
                e.Handled = true; // Evitamos que la app se cierre abruptamente
            };
            #endregion

            instance = this;
            mainModel = new MainModel();
            _handler = new MainHandler(this);

            // Configurar el Toolbar
            Toolbar toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            // Inicializar BottomNavigationView
            BottomNavigation = FindViewById<BottomNavigationView>(Resource.Id.navigation);
            BottomNavigation.SetOnNavigationItemSelectedListener(this);

            usuario = Intent.GetStringExtra("usuario") ?? "N/A"; // ← Recibes el valor
            InitializeUI();
            CheckAndRequestPermissions();

            BtHelper = new BluetoothHelper(this);

            getTb_RFID_Catalogo();

            #region ScanService
            // Registro dinámico del receptor
            var filter = new IntentFilter("unitech.scanservice.data");
            RegisterReceiver(new ScanReceiver(), filter);
            #endregion

            #region BAJA CAJONES (BOTON FLOTANTE)
            fabMain = FindViewById<FloatingActionButton>(Resource.Id.fabMain);
            // Obtener dimensiones de pantalla
            var displayMetrics = Resources.DisplayMetrics;
            screenWidth = displayMetrics.WidthPixels;
            screenHeight = displayMetrics.HeightPixels;
            fabMain.Touch += FabMain_Touch;
            #endregion

            #region NFC
            _nfcAdapter = NfcAdapter.GetDefaultAdapter(this);

            nfcAdapter = NfcAdapter.GetDefaultAdapter(this);

            if (nfcAdapter == null)
            {
                Toast.MakeText(this, "NFC no soportado en este dispositivo", ToastLength.Long).Show();
                Finish();
                return;
            }

            nfcPendingIntent = PendingIntent.GetActivity(
                this,
                0,
                new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
                PendingIntentFlags.Mutable);

            var nfcIntentFilter = new IntentFilter(NfcAdapter.ActionTagDiscovered);
            nfcIntentFilters = new IntentFilter[] { nfcIntentFilter };

            nfcIntentFilters = new IntentFilter[] {
                new IntentFilter(NfcAdapter.ActionTagDiscovered),
                new IntentFilter(NfcAdapter.ActionNdefDiscovered),
                new IntentFilter(NfcAdapter.ActionTechDiscovered),
                new IntentFilter(Intent.CategoryDefault),
            };

            techLists = new string[][] {
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.IsoDep)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.NfcA)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.Ndef)).Name },
                new string[] { Java.Lang.Class.FromType(typeof(Android.Nfc.Tech.MifareClassic)).Name }
            };
            #endregion

            // Inicializar el lector y comenzar monitoreo
            await InitializeReader();
            if (IsReaderConnected)
            {
                Task.Run(() => MonitorReaderStatus());
            }
            //await AjustarItemsSegunServidorAsync();
        }

        #region METODOS PARA MOSTRAR Y OCULTAR ELEMENTOS UI
        public void OcultarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Gone;
            fabMain.Visibility = ViewStates.Gone;
        }

        public void MostrarElementosNavegacion()
        {
            BottomNavigation.Visibility = ViewStates.Visible;
            fabMain.Visibility = ViewStates.Visible;
        }
        #endregion

        #region MOVIMIENTO BOTON FLOTANTE
        private void FabMain_Touch(object sender, View.TouchEventArgs e)
        {
            switch (e.Event.Action)
            {
                case MotionEventActions.Down:
                    dX = fabMain.GetX() - e.Event.RawX;
                    dY = fabMain.GetY() - e.Event.RawY;
                    lastAction = (int)e.Event.Action;
                    break;

                case MotionEventActions.Move:
                    float newX = e.Event.RawX + dX;
                    float newY = e.Event.RawY + dY;

                    // Limitar X para que no salga de la pantalla
                    if (newX < 0) newX = 0;
                    if (newX > screenWidth - fabMain.Width) newX = screenWidth - fabMain.Width;

                    // Limitar Y para que no salga de la pantalla
                    if (newY < 0) newY = 0;
                    if (newY > screenHeight - fabMain.Height - GetNavigationBarHeight())
                        newY = screenHeight - fabMain.Height - GetNavigationBarHeight();

                    fabMain.Animate()
                        .X(newX)
                        .Y(newY)
                        .SetDuration(0)
                        .Start();

                    lastAction = (int)e.Event.Action;
                    break;

                case MotionEventActions.Up:
                    if (lastAction == (int)MotionEventActions.Down)
                    {
                        // Abrir el diálogo con BajaRFID.xml
                        MostrarDialogoBajaRFID();
                    }
                    else
                    {
                        // Auto-pegar al borde más cercano
                        float midScreen = screenWidth / 2;
                        float finalX = fabMain.GetX() < midScreen ? 0 : screenWidth - fabMain.Width;

                        fabMain.Animate()
                            .X(finalX)
                            .SetDuration(200)
                            .Start();
                    }
                    break;
            }

            e.Handled = true;
        }

        // Obtener tamaño de la barra de navegación para evitar solapamiento
        private int GetNavigationBarHeight()
        {
            int resourceId = Resources.GetIdentifier("navigation_bar_height", "dimen", "android");
            return resourceId > 0 ? Resources.GetDimensionPixelSize(resourceId) : 0;
        }
        #endregion

        #region MOSTRAR DIALOGO BAJA RFID
        private void MostrarDialogoBajaRFID1()
        {
            LayoutInflater inflater = LayoutInflater.From(this);
            View dialogView = inflater.Inflate(Resource.Layout.BajaRFID, null);

            // Acceder solo al GridView
            gvQR = dialogView.FindViewById<GridView>(Resource.Id.gvleidoBajaRFID);

            // Configurar adaptador si es necesario
            if (qrAdapter == null)
                qrAdapter = new myGVitemAdapter(this, qrList);

            gvQR.Adapter = qrAdapter;

            Android.App.AlertDialog.Builder builder = new Android.App.AlertDialog.Builder(this);
            builder.SetView(dialogView);
            builder.SetTitle("Baja RFID");
            builder.SetCancelable(false);
            builder.SetPositiveButton("Cerrar", (sender, args) =>
            {
                qrList.Clear();
                qrAdapter.NotifyDataSetChanged();
                bajaDialog = null;
                getTb_RFID_Catalogo();
            });

            bajaDialog = builder.Create();
            bajaDialog.Show();
        }
        private void MostrarDialogoBajaRFID()
        {
            LayoutInflater inflater = LayoutInflater.From(this);
            View dialogView = inflater.Inflate(Resource.Layout.BajaRFID, null);

            gvQR = dialogView.FindViewById<GridView>(Resource.Id.gvleidoBajaRFID);
            if (qrAdapter == null)
                qrAdapter = new myGVitemAdapter(this, qrList);

            gvQR.Adapter = qrAdapter;

            // Crear AlertDialog con estilo personalizado
            Android.App.AlertDialog.Builder builder =
    new Android.App.AlertDialog.Builder(this, Resource.Style.AppTheme_CustomAlertDialog);



            builder.SetView(dialogView);
            builder.SetCancelable(false);
            builder.SetPositiveButton("GUARDAR", (sender, args) =>
            {
                if (qrList.Count > 0)
                {
                    ActualizarEstatusRFID(qrList);
                }

                qrList.Clear();
                qrAdapter.NotifyDataSetChanged();
                bajaDialog?.Dismiss(); // Usa Dismiss para cerrar el diálogo
                bajaDialog = null;
                getTb_RFID_Catalogo();
            });
            bajaDialog = builder.Create();
            bajaDialog.SetTitle("Baja de Etiquetas RFID");

            bajaDialog.Show();

            //// Ajustar tamaño dinámicamente
            //var window = bajaDialog.Window;
            //if (window != null)
            //{
            //    var layoutParams = new Android.Views.WindowManagerLayoutParams();
            //    layoutParams.CopyFrom(window.Attributes);
            //    layoutParams.Width = (int)(Resources.DisplayMetrics.WidthPixels * 0.95);  // 95% ancho
            //    layoutParams.Height = (int)(Resources.DisplayMetrics.HeightPixels * 0.80); // 80% alto
            //    layoutParams.Gravity = Android.Views.GravityFlags.Center;
            //    window.Attributes = layoutParams;

            //    //// Forzar que el GridView no desborde el diálogo
            //    //gvQR.LayoutParameters = new LinearLayout.LayoutParams(
            //    //    LinearLayout.LayoutParams.MatchParent,
            //    //    LinearLayout.LayoutParams.WrapContent);
            //}
        }
        #endregion

        #region METODOS PARA SCANSERVICE
        public void ProcessQR(string qrText)
        {
            RunOnUiThread(() =>
            {
                // Solo actualizar la lista y el GridView, sin tocar EditText
                if (!qrList.Contains(qrText))
                {
                    if (validaQR(qrText))
                    {
                        qrText = qrText.Remove(qrText.Length - 1);
                        qrList.Add(qrText);
                        qrAdapter?.NotifyDataSetChanged();
                    }
                }
            });
        }
        #endregion
        #region VALIDAR LECTURA DE TAG VS CATALOGO
        private bool validaQR(string EPC)
        {
            if (Tb_RFID_Catalogo == null || Tb_RFID_Catalogo.Rows.Count == 0)
            {
                getTb_RFID_Catalogo();
                // El catálogo no está cargado o está vacío
                return false;
            }

            foreach (DataRow row in Tb_RFID_Catalogo.Rows)
            {
                if (row["IdClaveInt"] != System.DBNull.Value && row["IdClaveInt"].ToString().Trim() == EPC.Trim())
                {
                    // EPC encontrado en el catálogo
                    return true;
                }
            }

            // EPC no encontrado
            return false;
        }
        #endregion
        #region ACTUALIZAR BAJA DE CAJONES
        private void ActualizarEstatusRFID(List<string> qrList)
        {
            if (qrList == null || qrList.Count == 0)
            {
                Android.Widget.Toast.MakeText(this, "No hay etiquetas para actualizar", ToastLength.Short).Show();
                return;
            }

            try
            {
                // Construimos la consulta dinámica con parámetros
                using (SqlConnection conn = new SqlConnection(cadenaConexion))
                {
                    conn.Open();

                    // Generar parámetros dinámicos
                    var parametros = string.Join(",", qrList.Select((qr, i) => $"@p{i}"));
                    string query = $"UPDATE Tb_RFID_Catalogo SET IdStatus = '2' WHERE IdClaveInt IN ({parametros})";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        // Agregar parámetros para evitar SQL Injection
                        for (int i = 0; i < qrList.Count; i++)
                        {
                            cmd.Parameters.AddWithValue($"@p{i}", qrList[i]);
                        }

                        int filasAfectadas = cmd.ExecuteNonQuery();

                        Android.Widget.Toast.MakeText(this, $"{filasAfectadas} registros actualizados", ToastLength.Long).Show();
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Widget.Toast.MakeText(this, "Error al actualizar: " + ex.Message, ToastLength.Long).Show();
            }
        }
        #endregion

        #region REGLA DE HORARIO DE INVENTARIO
        private async Task AjustarItemsSegunServidorAsync()
        {
            var resultado = await HoraServidorService.ObtenerAsync();
            if (resultado == null)
            {
                Toast.MakeText(this, "Sin conexión con el servidor horario.", ToastLength.Short).Show();
                return;
            }

            // Guardar en la propiedad pública
            this.Privilegios = resultado;

            // Opcional: ocultar ítems del menú
            var menu = BottomNavigation.Menu;
            menu.FindItem(Resource.Id.navigation_inventario)?.SetVisible(resultado.MostrarInventario);
            menu.FindItem(Resource.Id.navigation_entradas)?.SetVisible(resultado.MostrarEntradas);
            menu.FindItem(Resource.Id.navigation_salidas)?.SetVisible(resultado.MostrarSalidas);
        }


        private async Task AjustarItemsSegunServidorAsync2()
        {
            var resultado = await HoraServidorService.ObtenerAsync();
            if (resultado == null)
            {
                Toast.MakeText(this, "Sin conexión — se usará configuración por defecto.", ToastLength.Short).Show();
                return;
            }

            var menu = BottomNavigation.Menu;

            var itemInventario = menu.FindItem(Resource.Id.navigation_inventario);
            var itemEntradas = menu.FindItem(Resource.Id.navigation_entradas);
            var itemSalidas = menu.FindItem(Resource.Id.navigation_salidas);

            itemInventario?.SetVisible(resultado.MostrarInventario);
            itemEntradas?.SetVisible(resultado.MostrarEntradas);
            itemSalidas?.SetVisible(resultado.MostrarSalidas);
        }
        #endregion

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            BtHelper?.OnActivityResult(requestCode, resultCode);
        }

        #region METODOS PARA BOTTOMNAVIGATIONVIEW
        // Método para deshabilitar ítems del BottomNavigationView
        public void DisableNavigationItems(params int[] itemIds)
        {
            if (BottomNavigation != null)
            {
                if (itemIds.Length == 0)
                {
                    Log.Debug(TAG, "Deshabilitando todos los ítems del BottomNavigationView");
                    for (int i = 0; i < BottomNavigation.Menu.Size(); i++)
                    {
                        BottomNavigation.Menu.GetItem(i).SetEnabled(false);
                    }
                }
                else
                {
                    foreach (int itemId in itemIds)
                    {
                        var item = BottomNavigation.Menu.FindItem(itemId);
                        if (item != null)
                        {
                            item.SetEnabled(false);
                        }
                    }
                }
            }
        }
        // Método para habilitar ítems del BottomNavigationView
        public void EnableNavigationItems(params int[] itemIds)
        {
            if (BottomNavigation != null)
            {
                if (itemIds.Length == 0)
                {
                    Log.Debug(TAG, "Habilitando todos los ítems del BottomNavigationView");
                    for (int i = 0; i < BottomNavigation.Menu.Size(); i++)
                    {
                        BottomNavigation.Menu.GetItem(i).SetEnabled(true);
                    }
                }
                else
                {
                    foreach (int itemId in itemIds)
                    {
                        var item = BottomNavigation.Menu.FindItem(itemId);
                        if (item != null)
                        {
                            item.SetEnabled(true);
                        }
                    }
                }
            }
        }
        public bool OnNavigationItemSelected(IMenuItem item)
        {
            // Solo procesar la selección si el ítem está habilitado
            if (!item.IsEnabled)
            {
                return false;
            }

            return item.ItemId switch
            {
                Resource.Id.navigation_inventario => SetFragment(FragmentType.Inventario),
                Resource.Id.navigation_entradas => SetFragment(FragmentType.Entradas),
                Resource.Id.navigation_salidas => SetFragment(FragmentType.Salidas),
                _ => false
            };
        }
        #endregion

        #region METODOS RFID CENTRALIZADO
        public async Task InitializeReader()
        {
            try
            {
                // Asegurar que Bluetooth esté habilitado
                bool bluetoothOk = await BtHelper.EnsureBluetoothAsync();
                if (!bluetoothOk)
                {
                    Log.Error("MainActivity", "Bluetooth no está habilitado. No se puede inicializar el lector.");
                    ShowToast("Bluetooth no está habilitado. Habilítalo para conectar el lector RFID.");
                    IsReaderConnected = false;
                    baseReader = null;
                    return;
                }

                // Liberar recursos previos
                if (baseReader != null)
                {
                    Log.Debug("MainActivity", "Liberando recursos del lector existente...");
                    try
                    {
                        baseReader.RfidUhf?.Stop();
                        baseReader.Disconnect();
                    }
                    catch (Exception e)
                    {
                        Log.Warn("MainActivity", $"Error al liberar recursos: {e.Message}");
                    }
                    baseReader = null;
                    IsReaderConnected = false;
                }

                Log.Debug("MainActivity", "Inicializando RG768Reader...");
                baseReader = new RG768Reader(ApplicationContext);
                baseReader.Connect();

                if (baseReader.State == ConnectState.Connected)
                {
                    AssertAntennaConnectionSafe(); // Verificar antena
                    IsReaderConnected = true;
                    ConfigureGunKeyCode();
                    Log.Debug("MainActivity", "Lector RFID conectado correctamente");
                    ShowToast("Lector RFID conectado");
                }
                else
                {
                    throw new Exception("Lector RFID no conectado");
                }
            }
            catch (ReaderException re)
            {
                Log.Error("MainActivity", $"Error específico del lector RFID: {re.Message}, Code: {re.Code}");
                //ShowToast($"Error al conectar el lector: {re.Message}. Verifica la antena.");
                IsReaderConnected = false;
                baseReader = null;
            }
            catch (Exception e)
            {
                Log.Error("MainActivity", $"Error general al conectar el lector: {e.Message}");
                //ShowToast($"Error al conectar el lector: {e.Message}. Verifica la antena.");
                IsReaderConnected = false;
                baseReader = null;
            }
        }
        private bool AssertAntennaConnectionSafe()
        {
            try
            {
                if (baseReader == null || baseReader.RfidUhf == null)
                {
                    return false;
                }

                int power = baseReader.RfidUhf.Power; // Verifica que el módulo responde
                Log.Debug("MainActivity", $"Antena conectada, potencia: {power}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("MainActivity", $"Antena desconectada: {e.Message}");
                return false;
            }
        }


        public async Task InitializeReader2()
        {
            try
            {
                if (baseReader == null)
                {
                    Log.Debug("MainActivity", "Inicializando RG768Reader...");
                    baseReader = new RG768Reader(ApplicationContext);
                    baseReader.Connect();

                    if (baseReader.State == ConnectState.Connected)
                    {
                        bool isAntennaConnected = true; // Reemplaza con el método real del SDK
                        if (isAntennaConnected)
                        {
                            IsReaderConnected = true;
                            ShowToast("Lector RFID conectado con antena activa");
                            ConfigureGunKeyCode();
                            Task.Run(() => MonitorReaderStatus());
                        }
                        else
                        {
                            throw new Exception("Antena RFID no detectada");
                        }
                    }
                    else
                    {
                        throw new Exception("El lector RFID no se conectó correctamente");
                    }
                }
            }
            catch (ReaderException re)
            {
                Log.Error("MainActivity", $"Error específico del lector RFID: {re.Message}, StackTrace: {re.StackTrace}");
                //ShowToast($"Error al conectar el lector: {re.Message}");
                IsReaderConnected = false;
                baseReader = null;
            }
            catch (Exception e)
            {
                Log.Error("MainActivity", $"Error general al conectar el lector: {e.ToString()}");
                //ShowToast($"Error al conectar el lector: {e.Message}");
                IsReaderConnected = false;
                baseReader = null;
            }
        }
        private bool _isMonitoringReader = false;
        private async Task MonitorReaderStatus()
        {
            if (_isMonitoringReader || baseReader == null)
                return;

            _isMonitoringReader = true;
            Log.Debug("MainActivity", "Monitoreando estado del lector RFID...");

            while (_isMonitoringReader && !IsDestroyed)
            {
                try
                {
                    if (baseReader == null || baseReader.State != ConnectState.Connected)
                    {
                        Log.Warn("MainActivity", "Lector RFID desconectado.");
                        //ShowToast("Lector RFID desconectado. Intentando reconectar...");
                        await InitializeReader();
                    }
                    else if (!AssertAntennaConnectionSafe())
                    {
                        Log.Warn("MainActivity", "Antena desconectada.");
                        //ShowToast("Antena desconectada. Reconectando lector...");
                        await InitializeReader(); // Fuerza reinicialización completa
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("MainActivity", $"Error en monitoreo: {ex.Message}");
                    //ShowToast("Error en lector. Intentando reconectar...");
                    await InitializeReader();
                }

                await Task.Delay(5000); // Intervalo de chequeo
            }

            _isMonitoringReader = false;
        }


        private async Task MonitorReaderStatus2()
        {
            if (_isMonitoringReader || baseReader == null || !IsReaderConnected)
                return;

            _isMonitoringReader = true;
            Log.Debug("MainActivity", "Iniciando monitoreo del estado del lector RFID...");

            while (_isMonitoringReader && baseReader != null)
            {
                try
                {
                    if (baseReader.State != ConnectState.Connected)
                    {
                        Log.Warn("MainActivity", "Lector RFID desconectado, intentando reinicializar...");
                        ShowToast("Lector RFID desconectado");
                        await InitializeReader();
                    }
                    else
                    {
                        bool isAntennaConnected = true; // Reemplaza con el método real
                        if (!isAntennaConnected)
                        {
                            Log.Warn("MainActivity", "Antena RFID desconectada...");
                            ShowToast("Antena RFID desconectada. Por favor, verifica la conexión.");
                            await InitializeReader();
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error("MainActivity", $"Error al monitorear el lector: {e.Message}");
                    ShowToast($"Error en el lector RFID: {e.Message}");
                    IsReaderConnected = false;
                    baseReader = null;
                    await InitializeReader();
                }
                await Task.Delay(5000);
            }
            _isMonitoringReader = false;
        }
        private void ConfigureGunKeyCode()
        {
            // Mueve la lógica de setUseGunKeyCode aquí
            string keyName = "";
            string keyCode = "";

            switch (Build.Device)
            {
                case "HT730":
                    keyName = "TRIGGER_GUN";
                    keyCode = "298";
                    break;
                case "PA768":
                    keyName = "SCAN_GUN";
                    keyCode = "294";
                    break;
                default:
                    Log.Debug("MainActivity", "Skip to set gun key code");
                    return;
            }

            sendUssScan(false);

            Log.Debug("MainActivity", "Export keyMappings");
            Bundle exportBundle = KeymappingCtrl.GetInstance(ApplicationContext).ExportKeyMappings(getKeymappingPath());
            Log.Debug("MainActivity", "Export keyMappings, result: " + exportBundle.GetString("errorMsg"));

            Log.Debug("MainActivity", "Enable KeyMapping");
            Bundle enableBundle = KeymappingCtrl.GetInstance(ApplicationContext).EnableKeyMapping(true);
            Log.Debug("MainActivity", "Enable KeyMapping, result: " + enableBundle.GetString("errorMsg"));

            tempKeyCode = KeymappingCtrl.GetInstance(ApplicationContext).GetKeyMapping(keyName);

            Log.Debug("MainActivity", "Set Gun Key Code: " + keyCode);
            bool wakeup = tempKeyCode.GetBoolean("wakeUp");
            Bundle[] broadcastDownParams = getParams(tempKeyCode.GetBundle("broadcastDownParams"));
            Bundle[] broadcastUpParams = getParams(tempKeyCode.GetBundle("broadcastUpParams"));
            Bundle[] startActivityParams = getParams(tempKeyCode.GetBundle("startActivityParams"));

            Bundle resultBundle = KeymappingCtrl.GetInstance(ApplicationContext).AddKeyMappings(
                keyName,
                keyCode,
                wakeup,
                MainReceiver.rfidGunPressed,
                broadcastDownParams,
                MainReceiver.rfidGunReleased,
                broadcastUpParams,
                startActivityParams
            );

            if (resultBundle.GetInt("errorCode") == 0)
            {
                Log.Debug("MainActivity", "Set Gun Key Code success");
            }
            else
            {
                Log.Error("MainActivity", "Set Gun Key Code failed: " + resultBundle.GetString("errorMsg"));
            }
        }

        private void sendUssScan(bool enable)
        {
            Intent intent = new Intent();
            intent.SetAction(systemUssTriggerScan);
            intent.PutExtra(ExtraScan, enable);
            SendBroadcast(intent);
        }

        private string getKeymappingPath()
        {
            string defaultKeyConfigPath = keymappingPath;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
            {
                defaultKeyConfigPath = android12keymappingPath;
            }
            Log.Warn("MainActivity", defaultKeyConfigPath);
            return defaultKeyConfigPath;
        }

        private Bundle[] getParams(Bundle bundle)
        {
            if (bundle == null)
            {
                return null;
            }
            Bundle[] paramArray = new Bundle[bundle.KeySet().Count];
            int i = 0;
            foreach (string key in bundle.KeySet())
            {
                Bundle tmp = new Bundle();
                tmp.PutString("Key", key);
                tmp.PutString("Value", bundle.GetString(key));
                paramArray[i++] = tmp;
            }
            return paramArray;
        }
        protected override void OnDestroy()
        {
            _isMonitoringReader = false; // Detener monitoreo
            if (baseReader != null && IsReaderConnected)
            {
                baseReader.RfidUhf?.Stop();
                baseReader.Disconnect();
                baseReader = null;
                IsReaderConnected = false;
            }

            base.OnDestroy();
        }

        public void AssertReader()
        {
            if (baseReader == null || !IsReaderConnected)
            {
                Log.Warn("MainActivity", "Lector RFID no conectado. Iniciando reconexión...");
                //ShowToast("Lector RFID no conectado. Intentando reconectar...");
                throw new Exception("Lector RFID no conectado");
            }

            if (baseReader.State != ConnectState.Connected)
            {
                Log.Warn("MainActivity", "Lector RFID no está en estado conectado. Iniciando reconexión...");
                //ShowToast("Lector RFID no conectado correctamente. Intentando reconectar...");
                throw new Exception("Lector RFID no conectado");
            }

            try
            {
                AssertAntennaConnectionSafe();
            }
            catch (Exception e)
            {
                Log.Error("MainActivity", $"Error al verificar antena: {e.Message}");
                //ShowToast("Antena RFID desconectada. Intentando reconectar...");
                throw; // Relanzar la excepción para que ReconnectReader la maneje
            }
        }
        public bool TryAssertReader()
        {
            try
            {
                if (baseReader == null || !IsReaderConnected)
                {
                    Log.Warn("MainActivity", "Lector RFID no conectado. Intentando reconectar...");
                    //ShowToast("Intentando reconectar lector...");
                    ReconnectReader(); // <-- método que tú tengas para reconectar
                    return false;
                }

                if (baseReader.State != ConnectState.Connected)
                {
                    Log.Warn("MainActivity", "Lector RFID no está en estado conectado. Intentando reconectar...");
                    //ShowToast("Intentando reconectar lector...");
                    ReconnectReader();
                    return false;
                }

                // Verificación de la antena
                AssertAntennaConnectionSafe();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"Error en TryAssertReader: {ex.Message}");
                //ShowToast("Error con la antena. Intentando reconectar...");
                ReconnectReader();
                return false;
            }
        }
        public async void ReconnectReader()
        {
            try
            {
                //Log.Info("MainActivity", "Reconectando lector RFID...");

                // Si ya estaba inicializado, lo cerramos primero
                if (baseReader != null)
                {
                    baseReader.Disconnect();
                    baseReader.Dispose();
                    baseReader = null;
                }

                // Esperamos un momento antes de reconectar (puede ser útil si se desconectó físicamente)
                await Task.Delay(3000); // 3 segundo

                // Vuelves a inicializar el lector como lo haces normalmente
                InitializeReader(); // <-- Este debe ser el método que usas para conectar inicialmente

                ShowToast("Reconexión del lector en proceso...");
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"Error al reconectar lector RFID: {ex.Message}");
                //ShowToast("Error al reconectar el lector RFID");
            }
        }
        #endregion

        #region NFC
        protected override void OnResume()
        {
            base.OnResume();
            // Cada vez que volvemos a MainActivity, restauramos el estado

            // Traemos el FAB siempre al frente
            fabMain.BringToFront();
            fabMain.Visibility = ViewStates.Visible;
            #region NFC
            //if (_nfcAdapter == null)
            //{
            //    var alert = new Android.App.AlertDialog.Builder(this).Create();
            //    alert.SetMessage("NFC is not supported on this device.");
            //    alert.SetTitle("NFC Unavailable");
            //    alert.Show();
            //}
            //else
            //{
            //    //Set events and filters
            //    var tagDetected = new IntentFilter(NfcAdapter.ActionTagDiscovered);
            //    var ndefDetected = new IntentFilter(NfcAdapter.ActionNdefDiscovered);
            //    var techDetected = new IntentFilter(NfcAdapter.ActionTechDiscovered);
            //    var filters = new[] { ndefDetected, tagDetected, techDetected };

            //    var intent = new Intent(this, GetType()).AddFlags(ActivityFlags.SingleTop);

            //    var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable);

            //    // Gives your current foreground activity priority in receiving NFC events over all other activities.
            //    _nfcAdapter.EnableForegroundDispatch(this, pendingIntent, filters, null);
            //}


            //// Asegúrate de habilitar el despacho de primer plano
            //if (nfcAdapter != null && nfcAdapter.IsEnabled)
            //{
            //    nfcAdapter.EnableForegroundDispatch(this, nfcPendingIntent, nfcIntentFilters, techLists);
            //}
            #endregion

            //// Aseguramos que el FAB exista y esté visible
            //if (fabMain != null)
            //{
            //    fabMain.Visibility = ViewStates.Visible;
            //    fabMain.BringToFront(); // Traer al frente
            //}

        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);

            // Verificar el Action recibido
            string action = intent.Action;
            Toast.MakeText(this, "Action recibida: " + action, ToastLength.Short).Show();

            if (NfcAdapter.ActionTagDiscovered.Equals(action) ||
                NfcAdapter.ActionNdefDiscovered.Equals(action) ||
                NfcAdapter.ActionTechDiscovered.Equals(action))
            {
                var tag = intent.GetParcelableExtra(NfcAdapter.ExtraTag) as Tag;
                if (tag != null)
                {
                    string tagId = BitConverter.ToString(tag.GetId()).Replace("-", "");
                    Toast.MakeText(this, "TAG detectado: " + tagId, ToastLength.Short).Show();

                    // Delegar al fragmento visible
                    var currentFragment = SupportFragmentManager.Fragments
                        .FirstOrDefault(f => f.IsVisible && f is BaseFragment) as BaseFragment;

                    if (currentFragment != null)
                    {
                        currentFragment.OnNfcTagScanned(tagId);
                    }
                }
            }
        }

        private static string LittleEndian(string num)
        {
            // Convertir la cadena hexadecimal a Int64 (puedes usar UInt64 si los números son sin signo)
            var number = Convert.ToInt64(num, 16);  // Cambiado de Int32 a Int64
            var bytes = BitConverter.GetBytes(number);
            return bytes.Aggregate("", (current, b) => current + b.ToString("X2"));
        }

        public static string ByteArrayToString(byte[] ba)
        {
            var shb = new SoapHexBinary(ba);
            return shb.ToString();
        }
        #endregion

        #region CATALOGOS
        public void getTb_RFID_Catalogo()
        {
            try
            {
                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    const string query = "SELECT * FROM Tb_RFID_Catalogo WHERE IdStatus = 1";

                    using (SqlDataAdapter da = new SqlDataAdapter(query, thisConnection))
                    {
                        // Limpiar dataset y validar la tabla antes de asignarla
                        ds.Clear();
                        da.Fill(ds, "Tb_RFID_Catalogo");

                        if (ds.Tables.Contains("Tb_RFID_Catalogo") && ds.Tables["Tb_RFID_Catalogo"].Rows.Count > 0)
                        {
                            Tb_RFID_Catalogo = ds.Tables["Tb_RFID_Catalogo"];
                        }
                        else
                        {
                            Tb_RFID_Catalogo = null;
                            Toast.MakeText(this, "Catálogo vacío o no disponible", ToastLength.Short).Show();
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                // Error específico de SQL
                Toast.MakeText(this, "Error SQL al cargar catálogo: " + sqlEx.Message, ToastLength.Long).Show();
            }
            catch (Exception ex)
            {
                // Otros errores generales
                Toast.MakeText(this, "Error general al cargar catálogo: " + ex.Message, ToastLength.Long).Show();
            }
        }
        #endregion

        private void InitializeUI()
        {
            textMessage = FindViewById<TextView>(Resource.Id.message);
            var navigation = FindViewById<BottomNavigationView>(Resource.Id.navigation);
            navigation.SetOnNavigationItemSelectedListener(this);

            if (SupportFragmentManager.Fragments.Count == 0 && (usuario == "DESCARGUE" || usuario == "SISTEMAS"))
            {
                SwitchFragment(FragmentType.Verificacion);

            }
            else
            {
                SwitchFragment(FragmentType.Inventario);
            }

        }

        protected override void OnPause()
        {
            base.OnPause();

            // Deshabilitar el despacho de primer plano cuando la actividad no esté en primer plano
            if (nfcAdapter != null && nfcAdapter.IsEnabled)
            {
                //nfcAdapter.DisableForegroundDispatch(this);
            }
        }

        private void SendTagToFragment(string tagId)
        {
            var currentFragment = SupportFragmentManager.FindFragmentById(Resource.Id.fragment_container);

            if (currentFragment is BaseFragment baseFragment)
            {
                baseFragment.OnNfcTagScanned(tagId);
            }
        }

        protected override void OnStop()
        {
            if (baseReader != null)
            {
                baseReader.RfidUhf?.Stop();
                baseReader.Disconnect();
                baseReader = null;
            }
            base.OnStop();
        }

        public override void OnBackPressed()
        {
            // 1. Si hay fragmentos y NO estamos en InventarioFragment, volvemos a él primero
            var inventarioFragment = SupportFragmentManager.Fragments
                .FirstOrDefault(f => f is InventarioFragment);

            if (inventarioFragment != null && !inventarioFragment.IsVisible)
            {
                SwitchFragment(FragmentType.Inventario);
                return;
            }

            // 2. Mostramos un diálogo de confirmación antes de cerrar sesión y reiniciar
            new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                .SetTitle("Cerrar sesión")
                .SetMessage("¿Deseas cerrar sesión y reiniciar la aplicación?")
                .SetCancelable(true)
                .SetPositiveButton("Sí", (sender, args) =>
                {
                    ReiniciarAplicacion();
                })
                .SetNegativeButton("Cancelar", (sender, args) =>
                {
                    // No hacemos nada, simplemente cerramos el diálogo
                })
                .Show();
        }

        private void ReiniciarAplicacion()
        {
            // 1. Cerramos conexión RFID si existe
            if (baseReader != null)
            {
                try
                {
                    baseReader.RfidUhf?.Stop();
                    baseReader.Disconnect();
                    if (baseReader is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("RFID", $"Error cerrando baseReader: {ex.Message}");
                }
                finally
                {
                    baseReader = null;
                }
            }

            // 2. Eliminamos todos los fragmentos del back stack
            foreach (var fragment in SupportFragmentManager.Fragments)
            {
                SupportFragmentManager.BeginTransaction().Remove(fragment).CommitAllowingStateLoss();
            }

            // 3. Reiniciamos la app desde cero
            var intent = PackageManager.GetLaunchIntentForPackage(PackageName);
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
            StartActivity(intent);

            // 4. Cerramos la actividad actual
            Finish();
        }

        private void CheckAndRequestPermissions()
        {
            string[] requiredPermissions =
            {
                Manifest.Permission.AccessFineLocation,
                Manifest.Permission.AccessCoarseLocation
            };

            var missingPermissions = new List<string>();
            foreach (var permission in requiredPermissions)
            {
                if (CheckSelfPermission(permission) == Android.Content.PM.Permission.Denied)
                    missingPermissions.Add(permission);
            }

            if (missingPermissions.Count > 0)
            {
                ActivityCompat.RequestPermissions(this, missingPermissions.ToArray(), REQUEST_PERMISSION_CODE);
            }
            else
            {
                CheckBluetooth();
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == REQUEST_PERMISSION_CODE)
            {
                bool allGranted = Array.TrueForAll(grantResults, result => result == Android.Content.PM.Permission.Granted);
                if (allGranted) CheckBluetooth();
                else Finish();
            }
        }

        private void CheckBluetooth()
        {
            var bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
            if (bluetoothAdapter != null && !bluetoothAdapter.IsEnabled)
            {
                bluetoothAdapter.Enable();
            }
        }

        public void SwitchFragment(FragmentType fragmentType)
        {
            AndroidX.Fragment.App.Fragment fragment = fragmentType switch
            {
                FragmentType.Verificacion => new VerificacionFragment(),
                FragmentType.Entradas => new EntradasFragment(),
                FragmentType.Salidas => new SalidasFragment(),
                _ => new InventarioFragment()
            };

            //if (fragment != null)
            //{
            //    SupportFragmentManager.BeginTransaction()
            //        .Replace(Resource.Id.fragment_container, fragment)
            //        .Commit();
            //}

            SupportFragmentManager.BeginTransaction()
                .Replace(Resource.Id.fragment_container, fragment)
                .Commit();
        }

        //public bool OnNavigationItemSelected(IMenuItem item)
        //{
        //    return item.ItemId switch
        //    {
        //        Resource.Id.navigation_inventario => SetFragment(FragmentType.Inventario),
        //        Resource.Id.navigation_entradas => SetFragment(FragmentType.Entradas),
        //        Resource.Id.navigation_salidas => SetFragment(FragmentType.Salidas),
        //        _ => false
        //    };
        //}

        private bool SetFragment(FragmentType type)
        {
            SwitchFragment(type);
            return true;
        }

        public BaseFragment GetFragment(FragmentType fragmentType)
        {
            var fragmentsArray = SupportFragmentManager.Fragments;
            Type type = fragmentType switch
            {
                FragmentType.Entradas => typeof(EntradasFragment),
                FragmentType.Salidas => typeof(SalidasFragment),
                FragmentType.Inventario => typeof(InventarioFragment),
                FragmentType.Verificacion => typeof(VerificacionFragment),
                _ => null
            };

            if (type == null) return null;

            foreach (var fragment in fragmentsArray)
            {
                if (fragment.GetType() == type)
                {
                    return (BaseFragment)fragment;
                }
            }
            return null;
        }

        public static MainActivity getInstance()
        {
            return instance;
        }

        public static void ShowToast(string msg, bool lengthLong)
        {
            Message handlerMessage = new Message
            {
                What = (int)FragmentType.None,
                Data = new Bundle()
            };

            handlerMessage.Data.PutInt(ExtraName.HandleMsg, (int)HandlerMsg.Toast);
            handlerMessage.Data.PutString(ExtraName.Text, msg);
            handlerMessage.Data.PutInt(ExtraName.Number, lengthLong ? 1 : 0);

            _handler?.SendMessage(handlerMessage);
        }

        public static void ShowToast(string msg)
        {
            ShowToast(msg, true);
        }

        public static void ShowDialog(string title, string msg)
        {
            Message handlerMessage = new Message
            {
                What = (int)FragmentType.None,
                Data = new Bundle()
            };

            handlerMessage.Data.PutString(ExtraName.Title, title);
            handlerMessage.Data.PutString(ExtraName.Text, msg);
            handlerMessage.Data.PutInt(ExtraName.HandleMsg, (int)HandlerMsg.Dialog);

            _handler?.SendMessage(handlerMessage);
        }

        public static void TriggerHandler(FragmentType fragmentType, Bundle bundle)
        {
            try
            {
                AssertHandler();
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.Message);
                return;
            }

            Message handlerMessage = new Message();

            handlerMessage.What = (int)fragmentType;
            handlerMessage.Data = bundle;

            _handler.SendMessage(handlerMessage);
        }

        public static void AssertHandler()
        {
            if (_handler == null)
            {
                throw new Exception("Handler is not ready");
            }
        }
    }
}

