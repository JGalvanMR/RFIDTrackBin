using Android.App;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;

namespace RFIDTrackBin
{
    public class myGVitemAdapter : BaseAdapter<string>
    {
        Activity _CurrentContext;
        List<string> _tagEPCList;

        public myGVitemAdapter(Activity currentContext, List<string> tagEPCList)
        {
            _CurrentContext = currentContext;
            _tagEPCList = tagEPCList;
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            try
            {
                var item = _tagEPCList[position];
                if (convertView == null)
                    convertView = _CurrentContext.LayoutInflater.Inflate(Resource.Layout.custGridViewItem, null);

                convertView.FindViewById<TextView>(Resource.Id.txtName).Text = item.Split('|')[0];
                convertView.FindViewById<TextView>(Resource.Id.txtAge).Text = item.Split('|')[1];
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Error en MyGVitemAdapter: {e.Message}");
            }

            return convertView;
        }

        public override int Count => _tagEPCList?.Count ?? 0;

        public override string this[int position] => _tagEPCList?[position];
    }
}
