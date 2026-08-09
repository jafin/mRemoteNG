using System;
using System.ComponentModel;
using System.Globalization;
using mRemoteNG.Resources.Language;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.Tools;

public static class LocalizedAttributes
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedCategoryAttribute(string value, int order = 1) : CategoryAttribute(value)
    {
        private const int MaxOrder = 10;
        private int _order = order > MaxOrder ? MaxOrder : order;

        protected override string GetLocalizedString(string value)
        {
            string orderPrefix = "";
            for (int x = 0; x <= MaxOrder - _order; x++)
            {
                orderPrefix += Convert.ToString("\t", CultureInfo.InvariantCulture);
            }

            return orderPrefix + Language.ResourceManager.GetString(value, CultureInfo.CurrentCulture);
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedDisplayNameAttribute(string value) : DisplayNameAttribute(value)
    {
        private bool _localized;

        public override string DisplayName
        {
            get
            {
                if (!_localized)
                {
                    _localized = true;
                    DisplayNameValue = Language.ResourceManager.GetString(DisplayNameValue, CultureInfo.CurrentCulture) ?? DisplayNameValue;
                }

                return base.DisplayName;
            }
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedDescriptionAttribute(string value) : DescriptionAttribute(value)
    {
        private bool _localized;

        public override string Description
        {
            get
            {
                if (!_localized)
                {
                    _localized = true;
                    DescriptionValue = Language.ResourceManager.GetString(DescriptionValue, CultureInfo.CurrentCulture) ?? DescriptionValue;
                }

                return base.Description;
            }
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedDefaultValueAttribute(string name) : DefaultValueAttribute(Language.ResourceManager.GetString(name, CultureInfo.CurrentCulture))
    {

        // This allows localized attributes in a derived class to override a matching
        // non-localized attribute inherited from its base class
        public override object TypeId => typeof(DefaultValueAttribute);
    }

    #region Special localization - with String.Format

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedDisplayNameInheritAttribute(string value) : DisplayNameAttribute(value)
    {
        private bool _localized;

        public override string DisplayName
        {
            get
            {
                if (!_localized)
                {
                    _localized = true;
                    DisplayNameValue = string.Format(CultureInfo.CurrentCulture, Language.FormatInherit,
                        Language.ResourceManager.GetString(DisplayNameValue, CultureInfo.CurrentCulture));
                }

                return base.DisplayName;
            }
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class LocalizedDescriptionInheritAttribute(string value) : DescriptionAttribute(value)
    {
        private bool _localized;

        public override string Description
        {
            get
            {
                if (!_localized)
                {
                    _localized = true;
                    DescriptionValue = string.Format(CultureInfo.CurrentCulture, Language.FormatInheritDescription,
                        Language.ResourceManager.GetString(DescriptionValue, CultureInfo.CurrentCulture));
                }

                return base.Description;
            }
        }
    }

    #endregion
}