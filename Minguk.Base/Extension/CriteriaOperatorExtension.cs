using DevExpress.Data.Filtering;

namespace Minguk.Base.Extension;

public static class CriteriaOperatorExtension
{
    public static Dictionary<string, object> Extract(this CriteriaOperator op)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        GroupOperator? opGroup = op as GroupOperator;
        if (ReferenceEquals(opGroup, null))
        {
            ExtractOne(dict, op);
        }
        else
        {
            if (opGroup.OperatorType == GroupOperatorType.And)
            {
                foreach (var opn in opGroup.Operands)
                {
                    ExtractOne(dict, opn);
                }
            }
        }
        return dict;
    }

    private static void ExtractOne(Dictionary<string, object> dict, CriteriaOperator op)
    {
        InOperator? inOperator = op as InOperator;
        BinaryOperator? opBinary = op as BinaryOperator;
        FunctionOperator? opFunction = op as FunctionOperator;

        if (!ReferenceEquals(inOperator, null))
        {
            OperandProperty? opProperty = inOperator.LeftOperand as OperandProperty;
            CriteriaOperator[] opValues = inOperator.Operands.ToArray();
            if (ReferenceEquals(opProperty, null)) return;
            dict.Add(opProperty.PropertyName, opValues);
        }
        else if (!ReferenceEquals(opBinary, null))
        {
            OperandProperty? opProperty = opBinary.LeftOperand as OperandProperty;
            OperandValue? opValue = opBinary.RightOperand as OperandValue;
            if (ReferenceEquals(opProperty, null) || ReferenceEquals(opValue, null)) return;
            dict.Add(opProperty.PropertyName, opValue.Value);
        }
        else if (!ReferenceEquals(opFunction, null))
        {
            if (opFunction.OperatorType == FunctionOperatorType.StartsWith
                || opFunction.OperatorType == FunctionOperatorType.EndsWith
                || opFunction.OperatorType == FunctionOperatorType.Contains)
            {
                OperandProperty? opProperty = opFunction.Operands[0] as OperandProperty;
                OperandValue? opValue = opFunction.Operands[1] as OperandValue;
                if (ReferenceEquals(opProperty, null) || ReferenceEquals(opValue, null)) return;
                dict.Add(opProperty.PropertyName, opValue.Value);
            }
        }
    }
}
