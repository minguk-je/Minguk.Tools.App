using Minguk.Base.Interface;

namespace Minguk.Base.Services;

public class BooleanService : IBooleanService
{
    public bool Value { get; set; }

    public BooleanService(bool value)
    {
        Value = value;
    }
}
