using DevExpress.Xpo;

namespace Minguk.Base.Database;

/// <summary>
/// 앱 전역에서 공유하는 XPO DataLayer 공급자.
///
/// ※ DevExpress 의 ServiceContainer 는 서비스를 <b>인터페이스 타입으로만</b> 조회할 수 있다.
///   (구현 클래스로 GetService 를 부르면 "Services can be only accessed via interface types" 예외)
///   그래서 구현체(<see cref="XpoDataLayerProvider"/>)와 별도로 이 인터페이스가 필요하다.
///
/// 등록 / 조회는 <see cref="XpoDataLayerProvider.ServiceName"/> 이름으로 한다.
/// </summary>
public interface IXpoDataLayerProvider
{
    /// <summary>
    /// 지금 이 순간의 전역 연결 문자열.
    /// 화면이 '내가 붙어 있는 연결'을 기억해 두었다가 계속 쓰려면 이 값을 보관하면 된다.
    /// </summary>
    string CurrentConnectionString { get; }

    /// <summary>현재 전역 연결에 해당하는 공유 DataLayer.</summary>
    IDataLayer GetDataLayer();

    /// <summary>
    /// 지정한 연결 문자열의 공유 DataLayer.
    /// 전역 연결이 바뀌어도 화면이 이전 연결을 계속 쓰고 싶을 때 쓴다.
    /// </summary>
    IDataLayer GetDataLayer(string connectionString);
}
