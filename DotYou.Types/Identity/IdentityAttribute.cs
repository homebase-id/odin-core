using System;
using Identity.DataType.Attributes;
using Newtonsoft.Json;

namespace DotYou.Types.Identity
{
    /// <summary>
    /// Summary description for Class1
    /// </summary>
    public class IdentityAttribute<T> where T : BaseAttribute
    {
        private T _value;
        private string _label;

        // User defined, e.g. vacation home, grocery credit card - or should the label be part of the value?
        public string Label { get { return _label; } set { _label = Label; } }

        // Class , e.g. credit card #, CVC, exp date
        public T Value { get { return _value; } set { _value = Value; } }

        // Obsoleted public AccessControlList<PermissionFlags> _acl = new AccessControlList<PermissionFlags>();

        public IdentityAttribute(string label, T obj)
        {
            _label = label;
            _value = obj;
        }

        public static explicit operator IdentityAttribute<T>(IdentityAttribute<NameAttribute> v)
        {
            throw new NotImplementedException();
        }
    }
}
