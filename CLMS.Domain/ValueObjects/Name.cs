using System.Text.RegularExpressions;

namespace CLMS.Domain.ValueObjects;

public class Name
{
    private string? _value;
    public string Value
    {
        get 
        { 
            return _value; 
        } 
        set
        {
            Validate(value);
            _value = value;
        } 
    }

    public Name(string value)
    {
        Validate(value);
        Value = value;
    }

    private static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Name cannot be null or empty.", nameof(value));
        }

        if (!Regex.IsMatch(value, @"^[\p{L}'\-]+(\s[\p{L}'\-]+)*$"))
        {
            throw new ArgumentException("Name can only contain letters, apostrophes, and hyphens.", nameof(value));
        }
    }
}

