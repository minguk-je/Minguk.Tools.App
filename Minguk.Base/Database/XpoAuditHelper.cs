using DevExpress.Xpo;

using NLog;

namespace Minguk.Base.Database;

/// <summary>
/// 저장 직전 audit 필드(create/update user·datetime)를 채운다.
///
/// 생성된 모델 파일(Designer)을 건드리지 않도록 ViewModel 바깥에서 처리한다.
/// XPO 메타데이터로 필드 존재를 확인하므로 해당 컬럼이 없는 테이블은 그냥 건너뛴다.
/// </summary>
public static class XpoAuditHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public const string CreateUserIdMember = "create_user_id";
    public const string CreateDateTimeMember = "create_datetime";
    public const string UpdateUserIdMember = "update_user_id";
    public const string UpdateDateTimeMember = "update_datetime";

    /// <summary>
    /// UnitOfWork 의 저장 대상 전체에 audit 값을 적용한다.
    /// 신규 행은 create/update 를 모두, 기존 행은 update 만 채운다. 삭제 대상은 손대지 않는다.
    /// </summary>
    /// <param name="unitOfWork">저장할 UnitOfWork</param>
    /// <param name="userId">기록할 사용자 ID</param>
    /// <param name="timestamp">기록할 시각. 생략하면 DateTime.Now</param>
    public static void Apply(UnitOfWork? unitOfWork, string userId, DateTime? timestamp = null)
    {
        if (unitOfWork == null)
            return;

        var now = timestamp ?? DateTime.Now;

        foreach (var item in unitOfWork.GetObjectsToSave())
        {
            if (item == null)
                continue;

            // 삭제 대상은 audit 를 손대지 않는다.
            if (item is XPBaseObject deleting && deleting.IsDeleted)
                continue;

            if (unitOfWork.IsNewObject(item))
            {
                SetValue(unitOfWork, item, CreateUserIdMember, userId);
                SetValue(unitOfWork, item, CreateDateTimeMember, now);
            }

            SetValue(unitOfWork, item, UpdateUserIdMember, userId);
            SetValue(unitOfWork, item, UpdateDateTimeMember, now);
        }
    }

    /// <summary>
    /// XPO 메타데이터로 멤버를 찾아 값을 넣는다.
    /// 멤버가 없거나 읽기전용이면 조용히 건너뛴다.
    /// </summary>
    public static void SetValue(Session session, object item, string propertyName, object value)
    {
        try
        {
            var member = session.GetClassInfo(item)?.FindMember(propertyName);

            if (member == null || member.IsReadOnly)
                return;

            member.SetValue(item, value);
        }
        catch (Exception ex)
        {
            Logger.Debug($"audit 필드 세팅 실패 : {propertyName} / {ex.Message}");
        }
    }
}
