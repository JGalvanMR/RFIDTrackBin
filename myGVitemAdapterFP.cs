using Android.App;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;

namespace RFIDTrackBin
{
    public class myGVitemAdapterFP : BaseAdapter<string>
    {
        Activity _CurrentContext;
        List<string> _fletesPendientesList;

        public myGVitemAdapterFP(Activity currentContext, List<string> fletesPendientesList)
        {
            _CurrentContext = currentContext;
            _fletesPendientesList = fletesPendientesList;
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            try
            {
                var item = _fletesPendientesList[position];
                if (convertView == null)
                    convertView = _CurrentContext.LayoutInflater.Inflate(Resource.Layout.custGridViewItemFP, null);

                convertView.FindViewById<TextView>(Resource.Id.txtName).Text = item.Split('|')[0];
                convertView.FindViewById<TextView>(Resource.Id.txtAge).Text = item.Split('|')[1];
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Error en MyGVitemAdapter: {e.Message}");
            }

            return convertView;
        }

        public override int Count => _fletesPendientesList?.Count ?? 0;

        public override string this[int position] => _fletesPendientesList?[position];
    }
}
