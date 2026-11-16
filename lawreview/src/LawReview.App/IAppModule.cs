namespace LawReview.App;

/// <summary>
/// 메인 창의 탭 하나로 붙는 기능 모듈.
/// 법규검토 외의 기능(예: 토지이용계획확인원 CAD 변환)은 이 인터페이스를 구현해
/// MainForm.Modules에 등록하는 것만으로 추가된다.
/// </summary>
public interface IAppModule
{
    string Title { get; }
    Control CreateControl();
}
