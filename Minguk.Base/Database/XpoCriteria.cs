using DevExpress.Data.Filtering;

namespace Minguk.Base.Database;

/// <summary>
/// XPO Criteria 조립 헬퍼. EF 의 Where 체인을 옮기면서 화면마다 반복되던 조각을 모았다.
/// </summary>
public static class XpoCriteria
{
    /// <summary>EF 의 string.Contains 를 XPO 조건으로 옮긴 것. (LIKE '%값%')</summary>
    public static CriteriaOperator Contains(string propertyName, string value)
    {
        return new FunctionOperator(
            FunctionOperatorType.Contains,
            new OperandProperty(propertyName),
            new OperandValue(value));
    }

    /// <summary>
    /// 아무것도 나오지 않는 조건. (마스터 미선택 시 디테일 비우기 등)
    ///
    /// BinaryOperator 는 피연산자가 null 이면 생성 시점에 ArgumentNullException 을 던진다.
    /// (컬럼 = null 을 만들려면 IsNullOperator 를 써야 한다)
    /// 여기서 필요한 건 "빈 결과"이므로 상수 비교(1 = 0)로 만든다.
    /// </summary>
    public static CriteriaOperator None()
    {
        return new BinaryOperator(new OperandValue(1), new OperandValue(0), BinaryOperatorType.Equal);
    }

    /// <summary>값이 비어 있지 않을 때만 '=' 조건을 And 로 덧붙인다.</summary>
    public static CriteriaOperator AndEqualIfNotEmpty(CriteriaOperator criteria, string propertyName, string? value)
    {
        return string.IsNullOrEmpty(value)
            ? criteria
            : CriteriaOperator.And(criteria, new BinaryOperator(propertyName, value));
    }

    /// <summary>값이 비어 있지 않을 때만 LIKE '%값%' 조건을 And 로 덧붙인다.</summary>
    public static CriteriaOperator AndContainsIfNotEmpty(CriteriaOperator criteria, string propertyName, string? value)
    {
        return string.IsNullOrEmpty(value)
            ? criteria
            : CriteriaOperator.And(criteria, Contains(propertyName, value));
    }
}
