using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Media;
using Android.Media.TV;
using Android.Nfc;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Htx;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Event;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Reader.Types;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Rpx;
using Com.Unitech.Lib.Transport;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
using Com.Unitech.StuhflBridge;
using Java.Lang;
using Java.Util;
using Java.Util.Logging;
using RFIDTrackBin.enums;
using RFIDTrackBin.Modal;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Exception = System.Exception;
using Math = Java.Lang.Math;
using StringBuilder = System.Text.StringBuilder;
using Thread = System.Threading.Thread;

namespace RFIDTrackBin.fragment
{
    public class SalidasFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener
    {
        static string TAG = typeof(SalidasFragment).Name;

        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";

        public int MAX_MASK = 2;
        private int NIBLE_SIZE = 4;

        bool accessTagResult;

        private bool _isFindTag = false;

        #region Button
        private Button btnGuardar;
        #endregion

        #region TextView
        TextView connectedState;
        TextView areaLectura;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
        #endregion

        #region Spinner
        Spinner sprProveedor;
        Spinner sprRancho;
        Spinner sprTabla;
        #endregion

        #region MediaPlayer
        MediaPlayer mediaPlayer;
        #endregion

        #region SoundPool
        private SoundPool soundPool;
        private int beepSoundId;
        #endregion

        MainReceiver mReceiver;

        Bundle tempKeyCode = null;

        GridView gvObject;
        private List<string> tagEPCList = new List<string>();
        private myGVitemAdapter adapter;

        DataSet ds = new DataSet();
        public static DataTable vwProveedor = new DataTable("vwProveedor");
        public static DataTable vwRanchos = new DataTable("vwRanchos");
        public static DataTable vwTablas = new DataTable("vwTablas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        SqlConnection thisConnection;
        string IdClaveTag;
        View vwSalidas;

        string prov_nombre;
        string rch_nombre;
        string tbl_nombre;

        string prov_clave;
        string rch_clave;
        string tbl_clave;

        IMenu _menu;

        int IdConse;

        string tipoMovimiento = "S";

        ProgressBar progressBar;
        RelativeLayout loadingOverlay;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.SalidasFragment, container, false);
        }

        public override async void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);
            //_activity.mainModel.deviceType = DeviceType.Rg768;

            #region VALIDAR HORARIO
            //if (_activity.Privilegios == null)
            //{
            //    Toast.MakeText(_activity, "Esperando horario del servidor…", ToastLength.Short).Show();
            //    _activity.SupportFragmentManager.PopBackStack();
            //    return;
            //}

            //if (!_activity.Privilegios.MostrarSalidas)
            //{
            //    Toast.MakeText(_activity, "Salidas disponibles de 7:00 a.m. a 4:00 a.m.", ToastLength.Long).Show();
            //    _activity.SupportFragmentManager.PopBackStack();
            //    return;
            //}
            #endregion

            // ①  Garantiza Bluetooth encendido
            bool ok = await MainActivity.BtHelper.EnsureBluetoothAsync();
            if (!ok)
            {
                Toast.MakeText(Activity, "Bluetooth es obligatorio para el inventario.", ToastLength.Short).Show();
                return;                         // ⚠️ Sal temprano si el usuario lo negó
            }

            FindViewById(view);
            loadProveedor(view);

            SetButtonClick();
            InitializeSoundPool();

            HasOptionsMenu = true;

            mReceiver = new MainReceiver(this);
            IntentFilter filter = new IntentFilter();
            filter.AddAction(MainReceiver.rfidGunPressed);
            filter.AddAction(MainReceiver.rfidGunReleased);
            _activity.RegisterReceiver(mReceiver, filter);

            adapter = new myGVitemAdapter(_activity, tagEPCList);
            gvObject.Adapter = adapter;

            sprProveedor.Enabled = true;
            sprRancho.Enabled = false;
            sprTabla.Enabled = false;
            btnGuardar.Enabled = false;
            // Habilitar ítems del BottomNavigationView
            _activity.EnableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_inventario);
            //sprAreas.SetOnTouchListener(new SpinnerTouchListener(_activity));

            //await Task.Run(ConnectTask);

            progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
            loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);
        }

        public void InitializeSoundPool()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                var audioAttributes = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.AssistanceSonification)
                    .SetContentType(AudioContentType.Sonification)
                    .Build();

                soundPool = new SoundPool.Builder()
                    .SetMaxStreams(5)
                    .SetAudioAttributes(audioAttributes)
                    .Build();
            }
            else
            {
                soundPool = new SoundPool(5, Stream.Music, 0);
            }

            // Carga el sonido desde Resources/raw
            beepSoundId = soundPool.Load(_activity, Resource.Drawable.beep, 1);
        }

        #region MenuInflater
        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.menu_salidas, menu);
            _menu = menu;
            // Deshabilitar los ítems al inicio
            menu.FindItem(Resource.Id.fletes_pendientes_salidas).SetEnabled(true);
            menu.FindItem(Resource.Id.inicio_salidas).SetEnabled(false);
            menu.FindItem(Resource.Id.final_salidas).SetEnabled(false);
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.inicio_salidas:
                    InsertarSalida(tipoMovimiento, ((MainActivity)Activity).usuario, prov_clave, rch_clave, tbl_clave, "A");
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(true);
                    btnGuardar.Enabled = true;
                    // Deshabilitar ítems del BottomNavigationView (excepto Salidas)
                    _activity.DisableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_inventario, Resource.Id.navigation_salidas);
                    return true;
                case Resource.Id.final_salidas:
                    ActualizarHoraCierre(IdConse);
                    UpdateFechaUltimoMovimiento(IdConse);
                    sprProveedor.SetSelection(0);
                    sprProveedor.Enabled = true;
                    sprRancho.SetSelection(0);
                    sprTabla.SetSelection(0);
                    btnGuardar.Enabled = false;
                    // Habilitar ítems del BottomNavigationView
                    _activity.EnableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_inventario, Resource.Id.navigation_salidas);
                    _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);
                    _menu?.FindItem(Resource.Id.final_salidas)?.SetEnabled(false);
                    ClearGridView();
                    totalAcumuladoINT = 0;
                    txtTotalAcumulado.Text = totalAcumuladoINT.ToString();
                    return true;
                default:
                    return base.OnOptionsItemSelected(item);
            }
        }

        public override async void OnResume()
        {
            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "SALIDAS";
            base.OnResume();

            _activity.currentRfidFragment = this;
            if (_activity.baseReader != null && _activity.IsReaderConnected)
            {
                _activity.baseReader.AddListener(this);
                _activity.baseReader.RfidUhf.AddListener(this); // Asegúrate de añadir el listener específico para RFID UHF
                //InitSetting(); // Reaplicar configuración del lector
            }
            await Task.Run(ConnectTask);
        }
        #endregion

        #region INICIAR SALIDA
        public int InsertarSalida(string tipoMovimiento, string usuario, string entProveedor, string entRancho, string entTabla, string entStatus)
        {
            IdConse = -1;

            string query = @"
        INSERT INTO [dbo].[Tb_RFID_Mstr] (TipoMov, FechaMov, Usuario, Prov_Clave, Ran_Clave, Tab_Clave, Mstr_Status)
        VALUES (@TipoMov, GETDATE(), @Usuario, @Prov_Clave, @Ran_Clave, @Tab_Clave, @Mstr_Status);
        SELECT SCOPE_IDENTITY();";

            using (SqlConnection connection = new SqlConnection(MainActivity.cadenaConexion))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TipoMov", tipoMovimiento);
                    command.Parameters.AddWithValue("@Usuario", usuario ?? (object)System.DBNull.Value);
                    command.Parameters.AddWithValue("@Prov_Clave", entProveedor ?? (object)System.DBNull.Value);
                    command.Parameters.AddWithValue("@Ran_Clave", entRancho ?? (object)System.DBNull.Value);
                    command.Parameters.AddWithValue("@Tab_Clave", entTabla ?? (object)System.DBNull.Value);
                    command.Parameters.AddWithValue("@Mstr_Status", entStatus);

                    try
                    {
                        connection.Open();
                        object result = command.ExecuteScalar();
                        if (result != null && int.TryParse(result.ToString(), out int id))
                        {
                            IdConse = id;
                        }

                        Toast.MakeText(Activity, "Inicio De Salida...", ToastLength.Short).Show();
                        sprProveedor.Enabled = false;
                        sprRancho.Enabled = false;
                        sprTabla.Enabled = false;
                    }
                    catch (Java.Lang.Exception ex)
                    {
                        MainActivity.ShowDialog("Error al iniciar salida en Base de Datos:", ex.Message);
                    }
                }
            }
            return IdConse;
        }
        #endregion
        #region FINALIZAR SALIDA
        public void ActualizarHoraCierre(int idConse)
        {
            string query = @"
        UPDATE Tb_RFID_Mstr
        SET HoraCierre = GETDATE()
        WHERE IdConse = @IdConse";

            using (SqlConnection connection = new SqlConnection(MainActivity.cadenaConexion))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@IdConse", idConse);
                    try
                    {
                        connection.Open();
                        int rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            //MainActivity.ShowToast("Hora de cierre registrada.");
                            Toast.MakeText(Activity, "Fin de Salida...", ToastLength.Long).Show();
                            txtTotalAcumulado.Text = "0";
                        }
                        else
                        {
                            MainActivity.ShowToast("No se encontró la salida para actualizar.");
                        }
                    }
                    catch (Exception ex)
                    {
                        MainActivity.ShowDialog("Error al actualizar HoraCierre:", ex.Message);
                    }
                }
            }
        }
        public void UpdateFechaUltimoMovimiento(int idConseInv)
        {
            try
            {
                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    thisConnection.Open();
                    string query2 = @"
                UPDATE c
                SET c.FechaUltimoMovimiento = d.MaxFechaCaptura
                FROM Tb_RFID_Catalogo c
                INNER JOIN (
                    SELECT IdClaveTag, MAX(FechaCaptura) AS MaxFechaCaptura
                    FROM Tb_RFID_Det
                    WHERE IdConseInv = @IdConseInv
                    GROUP BY IdClaveTag
                ) d ON c.IdClaveTag = d.IdClaveTag";
                    string query = @"
                                    UPDATE c
                                    SET c.FechaUltimoMovimiento = d.FechaCaptura
                                    FROM Tb_RFID_Catalogo c
                                    INNER JOIN Tb_RFID_Det d ON c.IdClaveInt = d.IdClaveInt
                                    WHERE d.IdConseInv = @IdConseInv";

                    using (SqlCommand command = new SqlCommand(query, thisConnection))
                    {
                        command.Parameters.AddWithValue("@IdConseInv", idConseInv);
                        int rowsAffected = command.ExecuteNonQuery();
                        //Log.Debug(TAG, $"Filas actualizadas en Tb_RFID_Catalogo: {rowsAffected}");
                        MainActivity.ShowToast($"{rowsAffected} filas actualizadas en el catálogo");
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                //Log.Error(TAG, $"Error SQL al actualizar FechaUltimoMovimiento: {sqlEx.Message}");
                MainActivity.ShowToast($"Error SQL al actualizar: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                //Log.Error(TAG, $"Error general al actualizar FechaUltimoMovimiento: {ex.Message}");
                MainActivity.ShowToast($"Error al actualizar: {ex.Message}");
            }
        }
        #endregion

        private void PauseReading()
        {
            if (_activity.baseReader != null && _activity.baseReader.Action != ActionState.Stop)
            {
                _activity.baseReader.RfidUhf.Stop();
            }
        }

        public override void OnPause()
        {
            if (_activity.currentRfidFragment == this)
            {
                _activity.currentRfidFragment = null;
            }

            if (_activity.baseReader != null)
            {
                _activity.baseReader.RemoveListener(this);
                _activity.baseReader.RfidUhf.RemoveListener(this);
            }

            try
            {
                _activity.UnregisterReceiver(mReceiver);
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.Message);
            }

            base.OnPause();
        }

        public override void OnDestroy()
        {
            _activity.baseReader?.RemoveListener(this);
            restoreGunKeyCode();
            base.OnDestroy();

            if (gvObject != null)
            {
                gvObject.Adapter = null;
                gvObject.Dispose();
                gvObject = null;
            }

            if (adapter != null)
            {
                adapter.Dispose();
                adapter = null;
            }

            tagEPCList.Clear();
            tagEPCList = null;
        }

        private async Task ReconnectReader()
        {
            int maxRetries = 3;
            int retryCount = 0;

            while (retryCount < maxRetries && !_activity.IsReaderConnected)
            {
                try
                {
                    await _activity.InitializeReader();
                    break;
                }
                catch (Exception e)
                {
                    retryCount++;
                    Log.Error(TAG, $"Intento {retryCount} fallido: {e.Message}");
                    await Task.Delay(1000); // Esperar 1 segundo antes de reintentar
                }
            }

            if (!_activity.IsReaderConnected)
            {
                MainActivity.ShowToast("No se pudo reconectar el lector RFID");
            }
        }

        #region RFID
        public void OnNotificationState(NotificationState state, Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Object @params)
        {
            try
            {
                if (state == ActionState.Inventory6c)
                {
                    if (_isFindTag)
                        UpdateText(IDType.Find, GetString(Resource.String.stop));
                    else
                        UpdateText(IDType.Inventory, GetString(Resource.String.stop));
                }
                else if (state == ActionState.Stop)
                {
                    UpdateText(IDType.Inventory, GetString(Resource.String.inventory));
                    UpdateText(IDType.Find, GetString(Resource.String.find));
                }
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.Message);
            }
        }

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public async void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Object @params)
        {
            try
            {
                if (!TryAssertReader())
                {
                    Log.Warn("EntradasFragment", "No se pudo validar el lector. Se cancelará la operación.");
                    return; // Cancela la operación actual
                }
                //AssertReader();
                await Task.Run(ConnectTask);
            }
            catch (Exception e)
            {
                MainActivity.ShowToast(e.Message);
                return;
            }

            // Validar que todos los spinners tengan una selección válida
            bool proveedorSeleccionado = !sprProveedor.Enabled && sprProveedor.SelectedItemPosition > 0;
            bool ranchoSeleccionado = !sprRancho.Enabled && sprRancho.SelectedItemPosition > 0;
            bool tablaSeleccionada = !sprTabla.Enabled && sprTabla.SelectedItemPosition > 0;

            if (proveedorSeleccionado && ranchoSeleccionado && tablaSeleccionada && btnGuardar.Enabled)
            {
                if (type == KeyType.Trigger)
                {
                    if (state == KeyState.KeyDown && _activity.baseReader.Action == ActionState.Stop)
                    {
                        DoInventory();
                    }
                    else if (state == KeyState.KeyUp && _activity.baseReader.Action == ActionState.Inventory6c)
                    {
                        DoStop();
                    }
                }
            }
            else if (state == KeyState.KeyUp)
            {
                MainActivity.ShowDialog("AVISO", "Debe de dar Inicio a la captura de la salida y Seleccionar Provedor, Rancho y Tabla!");
            }

        }

        public void OnReaderStateChanged(BaseReader reader, ConnectState state, Object @params)
        {
            UpdateText(IDType.ConnectState, state.ToString());

            if (_activity != null && _activity.baseReader != null && _activity.baseReader.RfidUhf != null)
            {
                _activity.baseReader.RfidUhf.AddListener(this);
            }

            setUseGunKeyCode();

        }

        public void OnReaderTemperatureState(BaseReader reader, double temperatureState, Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public void OnRfidUhfAccessResult(BaseUHF uhf, ResultCode code, ActionState action, string epc, string data, Object @params)
        {
            if (code == ResultCode.NoError)
            {
                UpdateText(IDType.AccessResult, "Success");
            }
            else
            {
                UpdateText(IDType.AccessResult, code.ToString());
            }

            if (StringUtil.IsNullOrEmpty(data))
            {
                UpdateText(IDType.Data, "");
            }
            else
            {
                UpdateText(IDType.Data, data);
            }
            accessTagResult = (code == ResultCode.NoError);
        }

        public void OnRfidUhfReadTag(BaseUHF uhf, string tag, Object @params)
        {

            if (StringUtil.IsNullOrEmpty(tag))
            {
                return;
            }

            float rssi = 0;
            string tid = "";
            if (@params != null)
            {
                TagExtParam param = (TagExtParam)@params;
                rssi = param.Rssi;
                tid = param.TID;
            }

            if (!_isFindTag)
            {
                UpdateText(IDType.TagEPC, tag);
                UpdateText(IDType.TagTID, tid);
            }
            else
            {
                //int size = (int)((rssi - histogramMin) / (histogramMax - histogramMin) * (histogramSize));
                //////Log.Error("TAG", "rssi: " + rssi + ", size: " + (rssi - histogramMin) + "/" + (histogramMax - histogramMin) + "*" + histogramSize + "=" + size);
                //size = Math.Min(size, histogramSize);

                //histogramData.setData(size, rssi + "dBm");
                //histogramView.update(histogramData);
            }

            if (!string.IsNullOrEmpty(tag) && !tagEPCList.Contains(tag))
            {

                _activity.RunOnUiThread(() =>
                {
                    // Verifica si ya existe un tag con el mismo EPC en la lista
                    bool exists = tagEPCList.Any(item => item.StartsWith(tag + "|"));
                    if (!exists)
                    {
                        if (validaEPC(tag))
                        {
                            PlayBeepSound();
                            tagEPCList.Add(tag + "|" + rssi + " dBm");
                            adapter.NotifyDataSetChanged();
                            totalCajasLeidasINT++;
                            totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
                        }
                    }
                });
            }

            UpdateText(IDType.TagRSSI, rssi.ToString());
        }
        #endregion

        private void PlayBeepSound()
        {
            if (beepSoundId != 0)
            {
                soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f);
            }
        }

        private void FindViewById(View view)
        {
            //layoutDisplay = view.FindViewById<LinearLayout>(Resource.Id.layout_display);

            #region Button
            btnGuardar = view.FindViewById<Button>(Resource.Id.btnGuardar);
            #endregion

            //histogramView = view.FindViewById<HistogramView>(Resource.Id.histogram_view);

            //layoutTagTID = view.FindViewById<LinearLayout>(Resource.Id.layout_tagTID);

            //switchFastID = view.FindViewById<Switch>(Resource.Id.switch_fastId);

            #region TextView
            connectedState = view.FindViewById<TextView>(Resource.Id.txtConnectedState);
            totalCajasLeidas = view.FindViewById<TextView>(Resource.Id.txtNumTotalCajas);
            txtTotalAcumulado = view.FindViewById<TextView>(Resource.Id.txtNumTotalAcumulado);

            //temperature = view.FindViewById<TextView>(Resource.Id.temperature);
            //result = view.FindViewById<TextView>(Resource.Id.result);
            //tagEPC = view.FindViewById<TextView>(Resource.Id.tagEPC);
            //tagTID = view.FindViewById<TextView>(Resource.Id.tagTID);
            //tagRSSI = view.FindViewById<TextView>(Resource.Id.tagRSSI);
            //battery = view.FindViewById<TextView>(Resource.Id.battery);
            //tagData = view.FindViewById<TextView>(Resource.Id.tagData);
            #endregion

            #region Spinner
            sprProveedor = view.FindViewById<Spinner>(Resource.Id.sprProveedor);
            sprRancho = view.FindViewById<Spinner>(Resource.Id.sprRancho);
            sprTabla = view.FindViewById<Spinner>(Resource.Id.sprTabla);
            #endregion
            //editDisplay = view.FindViewById<EditText>(Resource.Id.edit_display);

            gvObject = view.FindViewById<GridView>(Resource.Id.gvleido);
        }

        #region SPINNERS
        #region Spinner Proveedor
        private void loadProveedor(View view)
        {
            try
            {
                vwSalidas = view;
                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    using (SqlDataAdapter da = new SqlDataAdapter("SELECT LTRIM(RTRIM(prov_clave)) AS prov_clave, LTRIM(RTRIM(prov_nombre)) AS prov_nombre FROM vwProveedor WHERE prov_clave IN (SELECT DISTINCT(prov_clave) FROM tb_mstr_recepcion_mp WHERE rmp_fecha >= CAST(DATEADD(DAY, -365, GETDATE()) AS DATE) AND rmp_fecha < DATEADD(DAY, 1, CAST(GETDATE() AS DATE))) UNION SELECT LTRIM(RTRIM(prov_clave)) AS prov_clave, LTRIM(RTRIM(prov_nombre)) AS prov_nombre FROM vwProveedor WHERE prov_clave IN (SELECT DISTINCT(prov_clave) FROM tb_mstr_recepcion_pt WHERE rpt_fecha >= CAST(DATEADD(DAY, -365, GETDATE()) AS DATE) AND rpt_fecha < DATEADD(DAY, 1, CAST(GETDATE() AS DATE))) UNION SELECT LTRIM(RTRIM(prov_clave)) AS prov_clave, LTRIM(RTRIM(prov_nombre)) AS prov_nombre FROM vwProveedor WHERE prov_clave IN (SELECT DISTINCT(prov_clave) FROM tb_mstr_recepcion_esparrago WHERE rmp_fecha >= CAST(DATEADD(DAY, -365, GETDATE()) AS DATE) AND rmp_fecha < DATEADD(DAY, 1, CAST(GETDATE() AS DATE))) ORDER BY LTRIM(RTRIM(PROV_NOMBRE)) ASC", thisConnection))
                    {
                        ds.Tables["vwProveedor"]?.Clear();
                        //ds.Clear(); // Asegúrate de limpiar el DataSet antes de llenarlo
                        da.Fill(ds, "vwProveedor");
                        vwProveedor = ds.Tables["vwProveedor"];
                    }
                }

                // Prepara los datos para el Spinner
                string[] strFrutas = new string[vwProveedor.Rows.Count + 1];
                strFrutas[0] = "Seleccione un Proveedor";
                for (int i = 1; i <= vwProveedor.Rows.Count; i++)
                {
                    strFrutas[i] = vwProveedor.Rows[i - 1]["prov_nombre"].ToString().Trim();
                }

                var comboAdapter = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, strFrutas);

                Spinner spinner2 = view.FindViewById<Spinner>(Resource.Id.sprProveedor);
                spinner2.ItemSelected -= sprProveedor_ItemSelected;
                spinner2.Adapter = comboAdapter;
                //Agrega el evento ItemSelected para manejar la selección de un ítem en el spinner
                spinner2.ItemSelected += sprProveedor_ItemSelected;

                spinner2.Enabled = true;
            }
            catch (Exception ex)
            {
                //Manejar excepciones
                Toast.MakeText(_activity, "Error al cargar los datos del spinner.", ToastLength.Long).Show();
            }
        }
        private void sprProveedor_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;

                // Obtener el item seleccionado con conversión segura
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem))
                {
                    if (e.Position > 0)
                    {
                        // Reiniciar spinners y claves dependientes
                        Spinner spinnerRancho = vwSalidas.FindViewById<Spinner>(Resource.Id.sprRancho);
                        Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);

                        spinnerRancho.SetSelection(0);
                        spinnerTabla.SetSelection(0);

                        rch_clave = "";
                        tbl_clave = "";

                        _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);

                        MainActivity.ShowDialog("Proveedor Seleccionado:", selectedItem.Trim());
                        //btnGuardar.Enabled = true;

                        prov_nombre = selectedItem;

                        prov_clave = getProv_Clave(prov_nombre);

                        //loadRanchosPorProveedor(prov_nombre);
                        loadRanchosPorProveedor(prov_clave);

                    }
                    else
                    {
                        Android.Util.Log.Info("Selección no válida", "No se seleccionó un proveedor válido.");
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de proveedor: " + ex.Message);
            }
        }
        private string getProv_Clave(string prov_nombre)
        {
            string prov_clave = "";

            if (vwProveedor != null && vwProveedor.Rows.Count > 0)
            {
                DataRow[] datos = vwProveedor.Select($"prov_nombre = '{prov_nombre.Replace("'", "''")}'");

                if (datos.Length > 0)
                {
                    prov_clave = datos[0]["prov_clave"].ToString();
                }
            }

            return prov_clave.Trim();
        }
        #endregion
        #region Spinner Rancho
        private void loadRanchosPorProveedor(string nombreProveedor)
        {
            try
            {
                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    string query = @"SELECT LTRIM(RTRIM(rch_clave)) AS rch_clave, 
                                    LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(rch_nombre, CHAR(160), ''), CHAR(9), ''), CHAR(13), ''))) AS rch_nombre 
                                    FROM vwRanchos 
                                    WHERE prov_clave IN (@nombreProveedor) ORDER BY rch_nombre ASC";

                    string query2 = @"
                SELECT rch_clave, rch_nombre 
                FROM vwRanchos 
                WHERE prov_clave IN (
                    SELECT prov_clave 
                    FROM vwProveedor 
                    WHERE prov_nombre = @nombreProveedor
                )";

                    string query3 = @"
                            SELECT c.rch_nombre AS rch_nombre 
                            FROM vwTablas AS A 
                            INNER JOIN vwProveedor AS B ON A.prov_clave=B.prov_clave 
                            INNER JOIN vwRanchos AS C ON A.rch_clave = C.rch_clave AND C.prov_clave = B.prov_clave 
                            WHERE B.prov_clasificacion = 'MP' OR B.prov_clasificacion='VA' AND C.prov_nombre = @nombreProveedor 
                            GROUP BY B.prov_nombre 
                            ORDER BY prov_nombre ASC";

                    using (SqlCommand cmd = new SqlCommand(query, thisConnection))
                    {
                        cmd.Parameters.AddWithValue("@nombreProveedor", nombreProveedor);
                        using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                        {
                            ds.Tables["vwRanchos"]?.Clear();
                            //ds.Clear(); // Asegúrate de limpiar antes de llenar
                            da.Fill(ds, "vwRanchos");
                            vwRanchos = ds.Tables["vwRanchos"];
                        }
                    }
                }

                string[] listaRanchos = new string[vwRanchos.Rows.Count + 1];
                listaRanchos[0] = "Seleccione un Rancho";

                for (int i = 1; i <= vwRanchos.Rows.Count; i++)
                {
                    listaRanchos[i] = vwRanchos.Rows[i - 1]["rch_nombre"].ToString().Trim();
                }

                var ranchosAdapter = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, listaRanchos);

                Spinner spinnerRanchos = vwSalidas.FindViewById<Spinner>(Resource.Id.sprRancho);
                spinnerRanchos.ItemSelected -= sprRancho_ItemSelected;
                spinnerRanchos.Adapter = ranchosAdapter;
                spinnerRanchos.ItemSelected += sprRancho_ItemSelected;

                spinnerRanchos.Enabled = true;

            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar ranchos: " + ex.Message, ToastLength.Long).Show();
            }
        }
        private void sprRancho_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;

                // Obtener el item seleccionado con conversión segura
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem))
                {
                    if (e.Position > 0)
                    {
                        // Reiniciar spinner de tablas y su clave
                        Spinner spinnerTabla = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);

                        spinnerTabla.SetSelection(0);

                        tbl_clave = "";

                        _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(false);

                        MainActivity.ShowDialog("Rancho Seleccionado:", selectedItem.Trim());
                        //btnGuardar.Enabled = true;

                        rch_nombre = selectedItem;

                        rch_clave = getRch_Clave(rch_nombre);

                        //loadTablasPorRancho(rch_nombre, prov_nombre);
                        loadTablasPorRancho(rch_clave, prov_clave);

                    }
                    else
                    {
                        Android.Util.Log.Info("Selección no válida", "No se seleccionó un rancho válido.");
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de rancho: " + ex.Message);
            }
        }
        private string getRch_Clave(string rch_nombre)
        {
            string rch_clave = "";

            DataRow[] datos = vwRanchos.Select("rch_nombre = '" + rch_nombre + "'");

            if (datos.Length > 0)
            {
                rch_clave = datos[0].ItemArray[0].ToString().Trim();
            }

            return rch_clave;
        }
        #endregion
        #region Spinner Tablas
        private void loadTablasPorRancho(string nombreRancho, string nombreProveedor)
        {
            try
            {
                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    string query = @"SELECT LTRIM(RTRIM(tbl_clave)) AS tbl_clave, LTRIM(RTRIM(tbl_nombre)) AS tbl_nombre FROM vwTablas WHERE rch_clave  IN (@nombreRancho) AND prov_clave IN (@nombreProveedor) ORDER BY tbl_nombre ASC";

                    string query2 = @"
                SELECT tbl_clave, tbl_nombre
                FROM vwTablas 
                WHERE rch_clave IN (
                    SELECT rch_clave 
                    FROM vwRanchos 
                    WHERE rch_nombre = @nombreRancho) AND
                    prov_clave IN (
                    SELECT prov_clave
                    FROM vwProveedor
                    WHERE prov_nombre = @nombreProveedor)";

                    string query3 = @"SELECT A.tbl_nombre AS tbl_nombre
FROM vwTablas AS A
INNER JOIN vwProveedor AS B ON A.prov_clave=B.prov_clave
INNER JOIN vwRanchos AS C ON A.rch_clave = C.rch_clave
AND C.prov_clave = B.prov_clave
WHERE B.prov_clasificacion = 'MP'
  OR B.prov_clasificacion='VA'
  AND B.prov_nombre = @nombreProveedor
  AND C.rch_nombre = @nombreRancho
GROUP BY B.prov_nombre
ORDER BY prov_nombre ASC";

                    using (SqlCommand cmd = new SqlCommand(query, thisConnection))
                    {
                        cmd.Parameters.AddWithValue("@nombreRancho", nombreRancho);
                        cmd.Parameters.AddWithValue("@nombreProveedor", nombreProveedor);
                        using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                        {
                            ds.Tables["vwTablas"]?.Clear();
                            //ds.Clear(); // Asegúrate de limpiar antes de llenar
                            da.Fill(ds, "vwTablas");
                            vwTablas = ds.Tables["vwTablas"];
                        }
                    }
                }

                string[] listaTablas = new string[vwTablas.Rows.Count + 1];
                listaTablas[0] = "Seleccione una Tabla";

                for (int i = 1; i <= vwTablas.Rows.Count; i++)
                {
                    listaTablas[i] = vwTablas.Rows[i - 1]["tbl_nombre"].ToString().Trim();
                }

                var tablasAdapter = new ArrayAdapter<string>(_activity, Android.Resource.Layout.SimpleSpinnerItem, listaTablas);

                Spinner spinnerTablas = vwSalidas.FindViewById<Spinner>(Resource.Id.sprTabla);
                spinnerTablas.ItemSelected -= sprTabla_ItemSelected;
                spinnerTablas.Adapter = tablasAdapter;
                spinnerTablas.ItemSelected += sprTabla_ItemSelected;

                spinnerTablas.Enabled = true;
            }
            catch (Exception ex)
            {
                Toast.MakeText(_activity, "Error al cargar tablas: " + ex.Message, ToastLength.Long).Show();
            }
        }
        private void sprTabla_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
        {
            try
            {
                Spinner spinner = (Spinner)sender;

                // Obtener el item seleccionado con conversión segura
                var selectedItem = spinner.GetItemAtPosition(e.Position)?.ToString();

                if (!string.IsNullOrEmpty(selectedItem))
                {
                    if (e.Position > 0)
                    {
                        MainActivity.ShowDialog("Tabla Seleccionada:", selectedItem.Trim());
                        //btnGuardar.Enabled = true;

                        tbl_nombre = selectedItem;

                        tbl_clave = getTbl_Clave(tbl_nombre);

                        _menu?.FindItem(Resource.Id.inicio_salidas)?.SetEnabled(true);
                    }
                    else
                    {
                        Android.Util.Log.Info("Selección no válida", "No se seleccionó una tabla válida.");
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("Error Spinner", "Error en selección de tabla: " + ex.Message);
            }
        }
        private string getTbl_Clave(string tbl_nombre)
        {
            string tbl_clave = "";

            DataRow[] datos = vwTablas.Select("tbl_nombre = '" + tbl_nombre + "'");

            if (datos.Length > 0)
            {
                tbl_clave = datos[0].ItemArray[0].ToString().Trim();
            }

            return tbl_clave;
        }
        #endregion
        #endregion

        #region BUTTONS
        private void SetButtonClicks()
        {
            btnGuardar.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (tagEPCList == null || tagEPCList.Count == 0)
                {
                    MainActivity.ShowToast("No hay datos para guardar.");
                    return;
                }

                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    thisConnection.Open(); // Abre la conexión una sola vez

                    using (SqlCommand cmd = new SqlCommand())
                    {
                        cmd.Connection = thisConnection;
                        cmd.CommandText = "INSERT INTO Tb_RFID_Catalogo (IdClaveTag, IdClaveInt, IdStatus) SELECT @IdClaveTag, '', '1' WHERE NOT EXISTS (SELECT 1 FROM Tb_RFID_Catalogo WHERE IdClaveTag = @IdClaveTag)";
                        cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar)); // Define el parámetro

                        foreach (string TAGID in tagEPCList)
                        {
                            IdClaveTag = TAGID.Split('|')[0];

                            // Asigna el valor al parámetro en cada iteración
                            cmd.Parameters["@IdClaveTag"].Value = IdClaveTag;

                            //cmd.ExecuteNonQuery();
                        }
                    }
                } // La conexión se cierra automáticamente aquí

                MainActivity.ShowDialog("INFORMACION ALMACENADA", "La informacion se a guardado de manera exitosa.");
                ClearGridView();
            };
        }

        private void SetButtonClick2()
        {
            btnGuardar.Click += delegate
            {
                try
                {
                    AssertReader();
                }
                catch (Exception e)
                {
                    MainActivity.ShowToast(e.Message);
                    return;
                }

                if (tagEPCList == null || tagEPCList.Count == 0)
                {
                    MainActivity.ShowToast("No hay datos para guardar.");
                    return;
                }

                int registrosInsertados = 0;

                using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                {
                    thisConnection.Open();

                    string query = @"
                INSERT INTO Tb_RFID_Det (IdConseInv, IdClaveInt, FechaCaptura)
                SELECT @IdConseInv, IdClaveInt, GETDATE()
                FROM Tb_RFID_Catalogo
                WHERE IdClaveTag = @IdClaveTag
                AND NOT EXISTS (
                    SELECT 1 FROM Tb_RFID_Det
                    WHERE IdClaveInt = Tb_RFID_Catalogo.IdClaveInt
                    AND IdConseInv = @IdConseInv
                )";

                    using (SqlCommand cmd = new SqlCommand(query, thisConnection))
                    {
                        cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar));
                        cmd.Parameters.Add(new SqlParameter("@IdConseInv", SqlDbType.Decimal)).Value = IdConse;

                        foreach (string TAGID in tagEPCList)
                        {
                            string IdClaveTag = TAGID.Split('|')[0];
                            cmd.Parameters["@IdClaveTag"].Value = IdClaveTag;

                            // Suma el número de filas insertadas
                            registrosInsertados += cmd.ExecuteNonQuery();
                        }
                    }
                }

                totalAcumuladoINT += registrosInsertados;
                txtTotalAcumulado.Text = totalAcumuladoINT.ToString();

                MainActivity.ShowDialog("INFORMACIÓN ALMACENADA", $"Se han guardado {registrosInsertados} registros exitosamente.");
                ClearGridView();
            };
        }
        private void SetButtonClick()
        {
            btnGuardar.Click += async (s, e) =>
            {
                try
                {
                    if (!TryAssertReader())
                    {
                        Log.Warn("EntradasFragment", "No se pudo validar el lector. Se cancelará la operación.");
                        return; // Cancela la operación actual
                    }
                    //AssertReader();
                    await Task.Run(ConnectTask);
                }
                catch (Exception ex)
                {
                    MainActivity.ShowToast(ex.Message);
                    return;
                }

                if (tagEPCList == null || tagEPCList.Count == 0)
                {
                    MainActivity.ShowToast("No hay datos para guardar.");
                    return;
                }

                // Mostrar ProgressBar y deshabilitar botón
                loadingOverlay.Visibility = ViewStates.Visible;
                btnGuardar.Enabled = false;

                int registrosInsertados = 0;

                try
                {
                    await Task.Run(() =>
                    {
                        using (SqlConnection thisConnection = new SqlConnection(MainActivity.cadenaConexion))
                        {
                            thisConnection.Open();

                            string query = @"
                        INSERT INTO Tb_RFID_Det (IdConseInv, IdClaveInt, FechaCaptura)
                        SELECT @IdConseInv, IdClaveInt, GETDATE()
                        FROM Tb_RFID_Catalogo
                        WHERE IdClaveTag = @IdClaveTag
                        AND NOT EXISTS (
                            SELECT 1 FROM Tb_RFID_Det
                            WHERE IdClaveInt = Tb_RFID_Catalogo.IdClaveInt
                            AND IdConseInv = @IdConseInv
                        )";

                            using (SqlCommand cmd = new SqlCommand(query, thisConnection))
                            {
                                cmd.Parameters.Add(new SqlParameter("@IdClaveTag", SqlDbType.VarChar));
                                cmd.Parameters.Add(new SqlParameter("@IdConseInv", SqlDbType.Decimal)).Value = IdConse;

                                foreach (string TAGID in tagEPCList)
                                {
                                    string IdClaveTag = TAGID.Split('|')[0];
                                    cmd.Parameters["@IdClaveTag"].Value = IdClaveTag;

                                    registrosInsertados += cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    });

                    totalAcumuladoINT += registrosInsertados;
                    txtTotalAcumulado.Text = totalAcumuladoINT.ToString();

                    MainActivity.ShowDialog("INFORMACIÓN ALMACENADA", $"Se han guardado {registrosInsertados} registros exitosamente.");
                    ClearGridView();
                }
                catch (Exception ex)
                {
                    MainActivity.ShowToast("Error al guardar: " + ex.Message);
                }
                finally
                {
                    // Ocultar ProgressBar y habilitar botón
                    loadingOverlay.Visibility = ViewStates.Gone;
                    btnGuardar.Enabled = true;
                }
            };
        }

        #endregion

        public override void ReceiveHandler(Bundle bundle)
        {
            UpdateUIType updateUIType = (UpdateUIType)bundle.GetInt(ExtraName.Type);

            switch (updateUIType)
            {
                case UpdateUIType.Text:
                    {
                        string data = bundle.GetString(ExtraName.Text);
                        IDType idType = (IDType)bundle.GetInt(ExtraName.TargetID);

                        switch (idType)
                        {
                            case IDType.ConnectState:
                                connectedState.Text = data;
                                break;
                            case IDType.Temperature:
                                //temperature.Text = data;
                                break;
                            case IDType.AccessResult:
                                //result.Text = data;
                                break;
                            case IDType.TagEPC:
                                ///tagEPC.Text = data;
                                break;
                            case IDType.TagTID:
                                //tagTID.Text = data;
                                break;
                            case IDType.TagRSSI:
                                //tagRSSI.Text = data;
                                break;
                            case IDType.Battery:
                                //battery.Text = data;
                                break;
                            case IDType.Inventory:
                                //buttonInventory.Text = data;
                                break;
                            case IDType.Data:
                                //tagData.Text = data;
                                break;
                            case IDType.Find:
                                //buttonFind.Text = data;
                                break;
                        }
                    }
                    break;
            }
        }

        private void AssertReader()
        {
            _activity.AssertReader();
        }
        private bool TryAssertReader()
        {
            return _activity.TryAssertReader();
        }

        private void AssertTagEPC(string epc)
        {
            if (StringUtil.IsNullOrEmpty(epc))
            {
                Log.Error(TAG, "EPC is empty");
                throw new Exception("EPC is empty");
            }
        }

        private void DoInventory()
        {
            try
            {
                //_activity.baseReader.Beeper = BeeperState.Medium;
                //InitSetting();

                //ClearSelectMask();

                //ClearGridView(); // Limpia la lista antes de empezar una nueva lectura
                _activity.baseReader.RfidUhf.Inventory6c();

                _isFindTag = false;
                _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));
                _activity.baseReader.RfidUhf.Inventory6c();

            }
            catch (ReaderException e)
            {
                MainActivity.ShowToast(e.Message);
            }


        }

        private void DoStop()
        {
            _isFindTag = false;
            _activity.baseReader.RfidUhf.Stop();
        }

        #region CONFIGURACIONES
        void InitSetting()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 150;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S3;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    //_activity.baseReader.RfidUhf.StartQ = 4;
                    //_activity.baseReader.RfidUhf.MaxQ = 15;
                    //_activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    _activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #region ORIGINAL
        void InitSettingS()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 150;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.StartQ = 4;
                    _activity.baseReader.RfidUhf.MaxQ = 15;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    _activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #region LONG RANGE
        void InitSetting1()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 1;
                    _activity.baseReader.RfidUhf.Power = 33;
                    _activity.baseReader.RfidUhf.InventoryTime = 100;
                    _activity.baseReader.RfidUhf.IdleTime = 100;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.FixedQ;
                    _activity.baseReader.RfidUhf.StartQ = 0;
                    _activity.baseReader.RfidUhf.MaxQ = 15;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = false;
                    //_activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #region DEFAULT
        void InitSetting2()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 100;
                    _activity.baseReader.RfidUhf.IdleTime = 10;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.StartQ = 4;
                    _activity.baseReader.RfidUhf.MaxQ = 15;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    //_activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #region RAPID READ
        void InitSetting3()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 3;
                    _activity.baseReader.RfidUhf.Power = 26;
                    _activity.baseReader.RfidUhf.InventoryTime = 500;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.StartQ = 4;
                    _activity.baseReader.RfidUhf.MaxQ = 15;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    //_activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #region CYCLE COUNT 
        void InitSetting4()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 100;
                    _activity.baseReader.RfidUhf.IdleTime = 100;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S1;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.StartQ = 4;
                    _activity.baseReader.RfidUhf.MaxQ = 11;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = false;
                    //_activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #region DEFAULT - TAG OPERATION 
        void InitSetting5()
        {
            try
            {
                if (_activity.baseReader?.RfidUhf != null)
                {
                    _activity.baseReader.RfidUhf.ModuleProfile = 0;
                    _activity.baseReader.RfidUhf.Power = 30;
                    _activity.baseReader.RfidUhf.InventoryTime = 150;
                    _activity.baseReader.RfidUhf.IdleTime = 0;
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.StartQ = 4;
                    _activity.baseReader.RfidUhf.MaxQ = 15;
                    _activity.baseReader.RfidUhf.MinQ = 0;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    //_activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "Error en InitSetting: " + e.Message);
                //MainActivity.ShowToast("Error al configurar el lector: " + e.Message);
            }
        }
        #endregion
        #endregion

        private async Task ConnectTask()
        {
            try
            {
                if (_activity.baseReader == null || !_activity.IsReaderConnected)
                {
                    await _activity.InitializeReader();
                }
                _activity.baseReader.AddListener(this);
                _activity.baseReader.RfidUhf.AddListener(this);
                InitSetting(); // Reaplicar configuración del lector
                UpdateText(IDType.ConnectState, "Connected");
            }
            catch (Exception e)
            {
                Log.Error(TAG, e.ToString());
                MainActivity.ShowToast("Connect exception: " + e.Message);
                await ReconnectReader();
            }
        }

        public bool SetSelectMask(string maskEpc)
        {
            SelectMask6cParam param = new SelectMask6cParam(
                    true,
                    Mask6cTarget.Sl,
                    Mask6cAction.Ab,
                    BankType.Epc,
                    0,
                    maskEpc,
                    maskEpc.Length * NIBLE_SIZE);
            try
            {
                for (int i = 0; i < MAX_MASK; i++)
                {
                    _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false);
                }
                _activity.baseReader.RfidUhf.SetSelectMask6c(0, param);
                Log.Debug(TAG, "setSelectMask success: " + param.ToString());
            }
            catch (ReaderException e)
            {
                Log.Error(TAG, "setSelectMask failed: \n" + e.Code.Message);
                MainActivity.ShowToast("setSelectMask failed");
                return false;
            }
            return true;
        }

        public void ClearSelectMask()
        {
            for (int i = 0; i < MAX_MASK; i++)
            {
                try
                {
                    _activity.baseReader.RfidUhf.SetSelectMask6cEnabled(i, false);
                    Log.Debug(TAG, "ClearSelectMask successful");
                }
                catch (ReaderException e)
                {
                    throw e;
                }
            }
        }

        private void UpdateText(IDType id, string data)
        {
            Utilities.UpdateUIText(FragmentType.Salidas, (int)id, data);
        }

        void ClearResult()
        {
            UpdateText(IDType.AccessResult, "");
            UpdateText(IDType.Data, "");
        }

        public void OnCustomActionReceived(Context context, Intent intent)
        {
            if (_activity.currentRfidFragment != this) return;

            string action = intent.Action;
            if (action.Equals(MainReceiver.rfidGunPressed))
            {
                if (_activity.baseReader != null)
                {
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyDown, null);
                }
            }
            else if (action.Equals(MainReceiver.rfidGunReleased))
            {
                if (_activity.baseReader != null)
                {
                    OnReaderKeyChanged(null, KeyType.Trigger, KeyState.KeyUp, null);
                }
            }
        }

        private void SetDisplayOutput(int pLine, bool bClear, string data)
        {
            const int MAX_CHARS = 16;
            DisplayOutput display = null;
            byte param = 0x00;

            if (data.Length < 16)
            {
                int pChar = (int)Math.Floor((MAX_CHARS - data.Length) / 2);
                param |= (byte)pChar;
            }

            if (bClear) param |= (byte)0x20;

            if (pLine < 1 || pLine > 3) param |= (byte)0x80;
            else param |= (byte)(pLine << 6);

            display = new DisplayOutput((sbyte)param, data);

            try
            {
                _activity.baseReader.SetDisplayOutput(display);
                Log.Debug(TAG, "SetDisplayOutput Success");
            }
            catch (ReaderException e)
            {
                throw new RuntimeException(e);
            }
        }

        private void sendUssScan(bool enable)
        {
            Intent intent = new Intent();
            intent.SetAction(systemUssTriggerScan);
            intent.PutExtra(ExtraScan, enable);
            MainActivity.getInstance().SendBroadcast(intent);
        }

        private string getKeymappingPath()
        {
            string defaultKeyConfigPath = keymappingPath;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat)
            {
                defaultKeyConfigPath = android12keymappingPath;
            }
            Log.Warn(TAG, defaultKeyConfigPath);
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

        private void setUseGunKeyCode()
        {
            if (tempKeyCode == null)
            {
                Task.Run(async () =>
                {
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
                            Log.Debug(TAG, "Skip to set gun key code");
                            return;
                    }

                    sendUssScan(false);

                    Log.Debug(TAG, "Export keyMappings");
                    Bundle exportBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).ExportKeyMappings(getKeymappingPath());
                    Log.Debug(TAG, "Export keyMappings, result: " + exportBundle.GetString("errorMsg"));

                    Log.Debug(TAG, "Enable KeyMapping");
                    Bundle enableBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).EnableKeyMapping(true);
                    Log.Debug(TAG, "Enable KeyMapping, result: " + enableBundle.GetString("errorMsg"));

                    tempKeyCode = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).GetKeyMapping(keyName);

                    Log.Debug(TAG, "Set Gun Key Code: " + keyCode);
                    bool wakeup = tempKeyCode.GetBoolean("wakeUp");
                    Bundle[] broadcastDownParams = getParams(tempKeyCode.GetBundle("broadcastDownParams"));
                    Bundle[] broadcastUpParams = getParams(tempKeyCode.GetBundle("broadcastUpParams"));
                    Bundle[] startActivityParams = getParams(tempKeyCode.GetBundle("startActivityParams"));

                    Bundle resultBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).AddKeyMappings(
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
                        Log.Debug(TAG, "Set Gun Key Code success");
                    }
                    else
                    {
                        Log.Error(TAG, "Set Gun Key Code failed: " + resultBundle.GetString("errorMsg"));
                    }
                });
            }
        }

        private void restoreGunKeyCode()
        {
            if (tempKeyCode != null)
            {
                Task.Run(async () =>
                {
                    Log.Debug(TAG, "restoreGunKeyCode start");
                    string keymappingPath = getKeymappingPath();
                    Bundle resultBundle = KeymappingCtrl.GetInstance(MainActivity.getInstance().ApplicationContext).ImportKeyMappings(keymappingPath);
                    Log.Debug(TAG, resultBundle.GetString("errorMsg"));

                    if (resultBundle.GetInt("errorCode") == 0)
                    {
                        Log.Debug(TAG, "restoreGunKeyCode success");
                    }
                    else
                    {
                        Log.Error(TAG, "restoreGunKeyCode failed: " + resultBundle.GetString("errorMsg"));
                    }
                    tempKeyCode = null;
                });
            }
        }

        public void ClearGridView()
        {
            _activity.RunOnUiThread(() =>
            {
                tagEPCList.Clear();
                adapter.NotifyDataSetChanged();
                totalCajasLeidasINT = 0;
                totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
            });
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            if (v.Id == sprProveedor.Id && e.Action == MotionEventActions.Down)
            {
                Toast.MakeText(_activity, "Debe de dar Inicio a la captura del salida!", ToastLength.Short).Show();
                return true;
            }
            return false;
        }

        #region VALIDAR LECTURA DE TAG VS CATALOGO
        private bool validaEPC(string EPC)
        {
            if (_activity.Tb_RFID_Catalogo == null || _activity.Tb_RFID_Catalogo.Rows.Count == 0)
            {
                _activity.getTb_RFID_Catalogo();
                // El catálogo no está cargado o está vacío
                return false;
            }

            foreach (DataRow row in _activity.Tb_RFID_Catalogo.Rows)
            {
                if (row["IdClaveTag"] != System.DBNull.Value && row["IdClaveTag"].ToString().Trim() == EPC.Trim())
                {
                    // EPC encontrado en el catálogo
                    return true;
                }
            }

            // EPC no encontrado
            return false;
        }

        #endregion
    }
}