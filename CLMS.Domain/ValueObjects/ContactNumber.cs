using System.Text.RegularExpressions;

namespace CLMS.Domain.ValueObjects;
public class ContactNumber
{
    private string _value;
    public string Value
    {
        get { return _value; }
        set
        {
            Validate(value);
            _value = value;
        }
    }

    public ContactNumber(string value)
    {
        Validate(value);
        _value = value;
    }

    private static void Validate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Contact Number cannot be null or empty", nameof(value));
        }

        if (!Regex.IsMatch(value, @"^\+?\d{10,15}$"))
        {
            throw new ArgumentException("Contact Number must be a valid phone number with 10 to 15 digits, optionally starting with a '+'", nameof(value));
        }
    }
}
