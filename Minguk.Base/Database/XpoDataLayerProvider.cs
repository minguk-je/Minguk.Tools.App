using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Metadata;

using NLog;

using System.Collections.Concurrent;

namespace Minguk.Base.Database;

/// <summary>
/// 앱 전역에서 XPO DataLayer 를 공유한다.
///
/// 화면마다 DataLayer 를 만들면 열린 탭 수만큼 DB 연결이 유지되고,
/// 같은 타입의 XPClassInfo 가 화면 수만큼 중복 생성된다.
/// 그래서 DataLayer 는 '연결 문자열 하나당 하나'로 두고 화면은 UnitOfWork 만 따로 갖는다.
/// (화면별 격리는 UnitOfWork 가 이미 보장한다)
///
/// 연결 프로파일을 바꾸면 연결 문자열이 달라지므로 그때 새 DataLayer 가 만들어지고,
/// 기존 DataLayer 는 아직 그것을 쓰고 있는 화면이 있을 수 있으므로 앱 종료까지 살려 둔다.
/// (프로파일 수만큼만 늘어나므로 상한이 있다. 사용 중인 DataLayer 를 중간에 Dispose 하면
///  그 위에 붙은 UnitOfWork 들이 한꺼번에 깨지기 때문에 일부러 이렇게 둔다.)
///
/// ※ 여러 화면이 동시에 쓰므로 <see cref="ThreadSafeDataLayer"/> 를 쓴다.
///    ThreadSafeDataLayer 는 사전(Dictionary)이 사용 시작 전에 모두 채워져 있어야 안전하다.
///    그래서 앱이 다루는 전체 타입을 생성자에서 한 번에 받아 등록한다.
/// </summary>
public sealed class XpoDataLayerProvider : IXpoDataLayerProvider, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// ServiceContainer 등록 / 조회 이름.
    /// 조회는 반드시 <see cref="IXpoDataLayerProvider"/> 로 해야 한다.
    /// (ServiceContainer 는 인터페이스 타입만 허용한다)
    /// </summary>
    public const string ServiceName = "XpoDataLayer";

    private readonly Func<string> connectionStringFactory;
    private readonly AutoCreateOption autoCreateOption;
    private readonly Lazy<XPDictionary> dictionary;

    private readonly ConcurrentDictionary<string, IDataLayer> dataLayers = new(StringComparer.Ordinal);

    private bool disposed;

    /// <param name="connectionStringFactory">XPO 연결 문자열을 만드는 함수. 호출 시점의 전역 연결을 따른다.</param>
    /// <param name="persistentTypes">앱이 다루는 XPO 타입 전체.</param>
    /// <param name="autoCreateOption">
    /// 기본값 None : 운영 DB 의 스키마를 절대 건드리지 않는다.
    /// (SchemaAlreadyExists 로 두면 XPO 가 테이블/컬럼을 만들거나 바꿀 수 있다.)
    /// </param>
    public XpoDataLayerProvider(Func<string> connectionStringFactory, Type[] persistentTypes, AutoCreateOption autoCreateOption = AutoCreateOption.None)
    {
        this.connectionStringFactory = connectionStringFactory ?? throw new ArgumentNullException(nameof(connectionStringFactory));
        this.autoCreateOption = autoCreateOption;

        if (persistentTypes == null)
            throw new ArgumentNullException(nameof(persistentTypes));

        // 리플렉션 비용이 큰 작업이라 한 번만 만들어 모든 DataLayer 가 공유한다.
        // XPDictionary 는 DataLayer 간 공유가 허용된다.
        this.dictionary = new Lazy<XPDictionary>(() =>
        {
            var reflectionDictionary = new ReflectionDictionary();
            reflectionDictionary.GetDataStoreSchema(persistentTypes);
            return reflectionDictionary;
        }, isThreadSafe: true);
    }

    /// <summary>
    /// 지금 이 순간의 전역 연결 문자열.
    /// 화면이 '내가 붙어 있는 연결'을 기억해 두었다가 계속 쓰려면 이 값을 보관하면 된다.
    /// </summary>
    public string CurrentConnectionString
    {
        get
        {
            var connectionString = connectionStringFactory();

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("XPO 연결 문자열이 비어 있습니다.");

            return connectionString;
        }
    }

    /// <summary>
    /// 현재 전역 연결에 해당하는 공유 DataLayer 를 얻는다.
    /// 같은 연결 문자열이면 항상 같은 인스턴스를 돌려준다.
    /// </summary>
    public IDataLayer GetDataLayer()
    {
        return GetDataLayer(CurrentConnectionString);
    }

    /// <summary>
    /// 지정한 연결 문자열의 공유 DataLayer 를 얻는다.
    /// 전역 연결이 바뀌어도 화면이 이전 연결을 계속 쓰고 싶을 때 쓴다.
    /// </summary>
    public IDataLayer GetDataLayer(string connectionString)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("XPO 연결 문자열이 비어 있습니다.", nameof(connectionString));

        return dataLayers.GetOrAdd(connectionString, Create);
    }

    private IDataLayer Create(string connectionString)
    {
        Logger.Info("XPO DataLayer 생성 (공유)");

        var provider = XpoDefault.GetConnectionProvider(connectionString, autoCreateOption);

        return new ThreadSafeDataLayer(dictionary.Value, provider);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var dataLayer in dataLayers.Values)
        {
            try
            {
                dataLayer.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug($"DataLayer 정리 실패 : {ex.Message}");
            }
        }

        dataLayers.Clear();
    }
}
