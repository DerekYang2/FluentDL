﻿using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace FluentDL.Helpers
{
    class DateToYearConverter : IValueConverter
    {
        // Converts YYYY-MM-DD to YYYY
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value.ToString().Substring(0, 4);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    class DateVerboseConverter : IValueConverter
    {
        // Converts YYYY-MM-DD to Month DD, YYYY
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return DateTime.Parse(value.ToString()).ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}