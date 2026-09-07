using Minguk.Base.Interface;

namespace Minguk.Base.Services;

public class StringService : IStringService
{
    public string Value { get; set; }

    public StringService(string value)
    {
        Value = value;
    }
}
