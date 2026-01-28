using Android.App;
using Android.Content;
using Android.Media;
using Android.Nfc;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Unitech.Api.Keymap;
using Com.Unitech.Lib.Diagnositics;
using Com.Unitech.Lib.Reader;
using Com.Unitech.Lib.Reader.Event;
using Com.Unitech.Lib.Reader.Params;
using Com.Unitech.Lib.Reader.Types;
using Com.Unitech.Lib.Transport.Types;
using Com.Unitech.Lib.Types;
using Com.Unitech.Lib.Uhf;
using Com.Unitech.Lib.Uhf.Event;
using Com.Unitech.Lib.Uhf.Params;
using Com.Unitech.Lib.Uhf.Types;
using Com.Unitech.Lib.Util.Diagnotics;
using RFIDTrackBin.enums;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Com.Unitech.Lib.Htx;
using Com.Unitech.Lib.Rgx;
using Com.Unitech.Lib.Rpx;
using Com.Unitech.Lib.Transport;
using Java.Lang;
using Java.Util;
using System.Runtime.InteropServices.ComTypes;
using Com.Unitech.StuhflBridge;
using Java.Util.Logging;
using Exception = System.Exception;
using Math = Java.Lang.Math;
using StringBuilder = System.Text.StringBuilder;
using Thread = System.Threading.Thread;
using RFIDTrackBin.Modal;
using Java.Nio.Channels;

namespace RFIDTrackBin.fragment
{
    public class VerificacionFragment : BaseFragment, IReaderEventListener, IRfidUhfEventListener, MainReceiver.IEventLitener, View.IOnTouchListener
    {
        #region DECLARACION DE VARIABLES
        static string TAG = typeof(InventarioFragment).Name;

        static string keymappingPath = "/storage/emulated/0/Android/data/com.unitech.unitechrfidsample";
        static string android12keymappingPath = "/storage/emulated/0/Unitech/unitechrfidsample/";
        static string systemUssTriggerScan = "unitech.scanservice.software_scankey";
        static string ExtraScan = "scan";

        public int MAX_MASK = 2;
        private int NIBLE_SIZE = 4;

        bool accessTagResult;

        private bool _isFindTag = false;

        #region Button
        private Button btnNuevo;
        #endregion

        #region TextView
        TextView connectedState;
        TextView areaLectura;
        TextView totalCajasLeidas;
        TextView txtTotalAcumulado;
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
        public static DataTable areas = new DataTable("areas");

        int totalCajasLeidasINT = 0;
        int totalAcumuladoINT = 0;

        SqlConnection thisConnection;
        string IdClaveTag;

        IMenu _menu;

        int IdConseInv;

        ProgressBar progressBar;
        RelativeLayout loadingOverlay;
        #endregion

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            return inflater.Inflate(Resource.Layout.VerificacionFragment, container, false);
        }

        public override async void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);

            // ①  Garantiza Bluetooth encendido
            bool ok = await MainActivity.BtHelper.EnsureBluetoothAsync();
            if (!ok)
            {
                Toast.MakeText(Activity, "Bluetooth es obligatorio para el inventario.", ToastLength.Short).Show();
                return;                         // ⚠️ Sal temprano si el usuario lo negó
            }

            FindViewById(view);
            //SetButtonClick();
            InitializeSoundPool();

            HasOptionsMenu = true;

            mReceiver = new MainReceiver(this);
            IntentFilter filter = new IntentFilter();
            filter.AddAction(MainReceiver.rfidGunPressed);
            filter.AddAction(MainReceiver.rfidGunReleased);
            _activity.RegisterReceiver(mReceiver, filter);

            adapter = new myGVitemAdapter(_activity, tagEPCList);
            gvObject.Adapter = adapter;

            //btnGuardarInventario.Enabled = false;

            // Habilitar ítems del BottomNavigationView
            _activity.EnableNavigationItems(Resource.Id.navigation_entradas, Resource.Id.navigation_salidas);
            //await Task.Run(ConnectTask);
            progressBar = view.FindViewById<ProgressBar>(Resource.Id.progressBarGuardar);
            loadingOverlay = view.FindViewById<RelativeLayout>(Resource.Id.loadingOverlay);

        }

        #region METODOS INICIALES
        private void PlayBeepSound()
        {
            if (beepSoundId != 0)
            {
                soundPool.Play(beepSoundId, 1.0f, 1.0f, 0, 0, 1.0f);
            }
        }
        private void FindViewById(View view)
        {
            #region Button
            //btnNuevo = view.FindViewById<Button>(Resource.Id.btnNuevo);
            #endregion

            #region TextView
            connectedState = view.FindViewById<TextView>(Resource.Id.txtConnectedStateInventario);
            areaLectura = view.FindViewById<TextView>(Resource.Id.txtAreaLecturaInventario);
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

            gvObject = view.FindViewById<GridView>(Resource.Id.gvleidoVerificacion);
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
        public override async void OnResume()
        {
            base.OnResume();

            ((AndroidX.AppCompat.App.AppCompatActivity)Activity).SupportActionBar.Title = "VERIFICACION";
            _activity?.OcultarElementosNavegacion();
            _activity.currentRfidFragment = this;
            if (_activity.baseReader != null && _activity.IsReaderConnected)
            {
                _activity.baseReader.AddListener(this);
                _activity.baseReader.RfidUhf.AddListener(this); // Asegúrate de añadir el listener específico para RFID UHF
                //InitSetting(); // Reaplicar configuración del lector
            }
            await Task.Run(ConnectTask);
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
                //_activity.baseReader.RfidUhf?.Stop(); // Detener cualquier operación activa
            }

            try
            {
                _activity.UnregisterReceiver(mReceiver);
            }
            catch (Exception e)
            {
                Log.Error(TAG, $"Error al desregistrar receiver: {e.Message}");
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

            // Desregistrar el receptor NFC
            //_activity.UnregisterReceiver(new NfcReceiver(this));
        }
        #endregion

        #region MenuInflater
        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.menu_verificacion, menu);
            _menu = menu;
            // Deshabilitar los ítems al inicio
            menu.FindItem(Resource.Id.inicio_verificacion).SetEnabled(true);

            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.inicio_verificacion:
                    _menu?.FindItem(Resource.Id.inicio_verificacion)?.SetEnabled(true);
                    ClearGridView();
                    return true;
                default:
                    return base.OnOptionsItemSelected(item);
            }
        }
        #endregion

        #region RFID
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
        public void OnNotificationState(NotificationState state, Java.Lang.Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public void OnReaderActionChanged(BaseReader reader, ResultCode retCode, ActionState state, Java.Lang.Object @params)
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

        public void OnReaderBatteryState(BaseReader reader, int batteryState, Java.Lang.Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public async void OnReaderKeyChanged(BaseReader reader, KeyType type, KeyState state, Java.Lang.Object @params)
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

        public void OnReaderStateChanged(BaseReader reader, ConnectState state, Java.Lang.Object @params)
        {
            UpdateText(IDType.ConnectState, state.ToString());

            if (_activity != null && _activity.baseReader != null && _activity.baseReader.RfidUhf != null)
            {
                _activity.baseReader.RfidUhf.AddListener(this);
            }


            setUseGunKeyCode();
        }

        public void OnReaderTemperatureState(BaseReader reader, double temperatureState, Java.Lang.Object @params)
        {
            //throw new System.NotImplementedException();
        }

        public void OnRfidUhfAccessResult(BaseUHF uhf, ResultCode code, ActionState action, string epc, string data, Java.Lang.Object @params)
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

        public void OnRfidUhfReadTag(BaseUHF uhf, string tag, Java.Lang.Object @params)
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
                        //PlayBeepSound();
                        //tagEPCList.Add(tag + "|" + rssi + " dBm");
                        //adapter.NotifyDataSetChanged();
                        //totalCajasLeidasINT++;
                        //totalCajasLeidas.Text = totalCajasLeidasINT.ToString();
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

        private Android.OS.Handler handler = new Android.OS.Handler(Looper.MainLooper);
        private bool pendingUpdate = false;

        public void OnRfidUhfReadTag2(BaseUHF uhf, string tag, Java.Lang.Object @params)
        {
            if (StringUtil.IsNullOrEmpty(tag)) return;

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

            if (!string.IsNullOrEmpty(tag) && !tagEPCList.Contains(tag))
            {
                _activity.RunOnUiThread(() =>
                {
                    bool exists = tagEPCList.Any(item => item.StartsWith(tag + "|"));
                    if (!exists && validaEPC(tag))
                    {
                        PlayBeepSound();
                        tagEPCList.Add(tag + "|" + rssi + " dBm");
                        totalCajasLeidasINT++;
                        totalCajasLeidas.Text = totalCajasLeidasINT.ToString();

                        if (!pendingUpdate)
                        {
                            pendingUpdate = true;
                            handler.PostDelayed(new Java.Lang.Runnable(() =>
                            {
                                adapter.NotifyDataSetChanged();
                                pendingUpdate = false;
                            }), 300);
                        }
                    }
                });
            }

            UpdateText(IDType.TagRSSI, rssi.ToString());
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

        private void DoInventory()
        {
            try
            {
                //InitSetting();
                //ClearSelectMask();
                //ClearGridView(); // Limpia la lista antes de empezar una nueva lectura
                _activity.baseReader.RfidUhf.Inventory6c();
                _isFindTag = false;
                _activity.baseReader.SetDisplayTags(new DisplayTags(ReadOnceState.Off, BeepAndVibrateState.On));

            }
            catch (ReaderException e)
            {
                MainActivity.ShowToast($"Error al iniciar inventario: {e.Message}");
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
                    _activity.baseReader.RfidUhf.Power = 30;//30
                    _activity.baseReader.RfidUhf.InventoryTime = 150;//150
                    _activity.baseReader.RfidUhf.IdleTime = 0;//0
                    _activity.baseReader.RfidUhf.Target = Target.A;
                    _activity.baseReader.RfidUhf.Session = Session.S0;
                    _activity.baseReader.RfidUhf.AlgorithmType = AlgorithmType.DynamicQ;
                    _activity.baseReader.RfidUhf.ToggleTarget = true;
                    _activity.baseReader.RfidUhf.ContinuousMode = true;
                    Log.Debug(TAG, "Configuración del lector aplicada correctamente");
                }
            }
            catch (ReaderException e)
            {
                //MainActivity.ShowToast($"Error al configurar el lector: {e.Message}. Verifica la antena.");
            }
        }
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
                    //Log.Debug(TAG, "ClearSelectMask successful");
                }
                catch (ReaderException e)
                {
                    throw e;
                }
            }
        }

        private void UpdateText(IDType id, string data)
        {
            Utilities.UpdateUIText(FragmentType.Inventario, (int)id, data);
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
            //if (v.Id == sprAreas.Id && e.Action == MotionEventActions.Down)
            //{
            //    Toast.MakeText(_activity, "Debe de dar Inicio a la captura del inventario!", ToastLength.Short).Show();
            //    return true;
            //}
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