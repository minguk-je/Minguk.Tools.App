using DevExpress.Xpo;

using NLog;

namespace Minguk.Base.Database;

/// <summary>
/// 화면 하나가 쓰는 XPO 편집 세션 묶음.
///
/// DataLayer 는 <see cref="XpoDataLayerProvider"/> 가 앱 전역에서 공유하므로 여기서 소유하지 않는다.
/// 이 클래스가 소유하는 것은 편집용 <see cref="UnitOfWork"/> 와 룩업용 <see cref="Session"/> 뿐이다.
///
/// 편집 UnitOfWork 와 룩업 Session 을 나눠 두는 이유는,
/// 콤보(룩업) 조회가 편집 중인 객체를 건드리거나 변경 추적을 오염시키지 않게 하기 위함이다.
///
/// <see cref="Reset"/> 은 세션만 새로 만든다. DB 재연결이나 메타데이터 재구성이 없으므로
/// 취소(Cancel)처럼 자주 불리는 경로에서도 부담이 없다.
/// </summary>
public sealed class XpoStore : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IXpoDataLayerProvider provider;

    public XpoStore(IXpoDataLayerProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>공유 DataLayer. 이 인스턴스가 소유하지 않으므로 Dispose 하지 않는다.</summary>
    public IDataLayer? DataLayer { get; private set; }

    /// <summary>마스터/디테일 편집용 세션. 저장은 이 UoW 하나로 한 번에 커밋된다.</summary>
    public UnitOfWork? UnitOfWork { get; private set; }

    /// <summary>콤보(룩업) 전용 읽기 세션. 편집 UoW 와 분리해 변경 추적을 오염시키지 않는다.</summary>
    public Session? LookupSession { get; private set; }

    /// <summary>이 스토어가 붙어 있는 연결 문자열. 아직 <see cref="Reset"/> 전이면 null.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>
    /// 현재 전역 연결로 편집 세션을 (재)생성한다.
    /// 전역 연결이 바뀐 뒤 이걸 부르면 새 연결로 갈아탄다.
    ///
    /// ※ UoW 인스턴스가 바뀌므로 ObjectChanged 구독과 바인딩 컬렉션도 새로 만들어야 한다.
    /// </summary>
    public void Reset()
    {
        ResetCore(provider.CurrentConnectionString);
    }

    /// <summary>
    /// 붙어 있던 연결을 그대로 둔 채 편집 세션만 (재)생성한다.
    ///
    /// 취소(Cancel) 처리가 이걸 쓴다. 취소는 DropChanges 가 아니라 세션 재생성으로 되돌리는데,
    /// (XPObject 의 지연삭제 GCRecord 가 DropChanges / Reload 로 안정적으로 복원되지 않는다)
    /// 그때 전역 연결을 다시 읽어버리면 사용자가 모르는 사이에 대상 DB 가 바뀐다.
    /// 연결을 갈아타는 것은 <see cref="Reset"/> 을 명시적으로 부르는 경로에서만 일어나야 한다.
    ///
    /// 몇 번을 불러도 안전하다.
    /// </summary>
    public void ResetKeepingConnection()
    {
        ResetCore(ConnectionString ?? provider.CurrentConnectionString);
    }

    private void ResetCore(string connectionString)
    {
        ReleaseSessions();

        var dataLayer = provider.GetDataLayer(connectionString);

        ConnectionString = connectionString;
        DataLayer = dataLayer;
        UnitOfWork = new UnitOfWork(dataLayer);
        LookupSession = new Session(dataLayer);
    }

    /// <summary>변경분을 커밋한다. UnitOfWork 가 없으면 아무것도 하지 않는다.</summary>
    public void CommitChanges()
    {
        UnitOfWork?.CommitChanges();
    }

    /// <summary>아직 DB 에 저장된 적 없는(신규) 객체인지.</summary>
    public bool IsNewObject(object? item)
    {
        return item != null && UnitOfWork != null && UnitOfWork.IsNewObject(item);
    }

    /// <summary>세션만 해제한다. 공유 DataLayer 는 건드리지 않는다.</summary>
    public void Dispose()
    {
        ReleaseSessions();
        DataLayer = null;
    }

    private void ReleaseSessions()
    {
        try
        {
            LookupSession?.Dispose();
            UnitOfWork?.Dispose();
        }
        catch (Exception ex)
        {
            // 정리 중 예외로 화면 종료가 막히지 않게 한다.
            Logger.Debug($"XpoStore 세션 정리 실패 : {ex.Message}");
        }
        finally
        {
            LookupSession = null;
            UnitOfWork = null;
        }
    }
}
