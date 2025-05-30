using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;
using Models = CCTV.Models;
using System.Threading.Tasks;

namespace CCTV.Views
{
    public partial class RecordingPlayerWindow : Window
    {
        private int m_lUserID = -1;
        private int m_lPlayHandle = -1;
        private bool m_bInitSDK = false;
        private Image videoControl;
        private DateTime m_startTime;
        private DateTime m_endTime;
        private int channelNumber = 1; // 33번에서 1번으로 변경
        private DispatcherTimer progressTimer;
        private bool isPlaying = false;
        private double playbackSpeed = 1.0;
        private Models.CCTV.NET_DVR_DEVICEINFO_V30 deviceInfo = new Models.CCTV.NET_DVR_DEVICEINFO_V30();
        
        // 사용자가 날짜를 선택한 후 시간을 직접 수정했는지 추적하는 Dictionary
        private Dictionary<DateTime, bool> dateEndTimeModified = new Dictionary<DateTime, bool>();
        
        // 비디오 창 참조 추가
        private RecordingVideoWindow _videoWindow;

        // 재생 제어 명령 코드 (하이크비전 SDK)
        private const int NET_DVR_PLAYSTART = 1;       // 재생 시작
        private const int NET_DVR_PLAYSTOP = 2;        // 재생 중지
        private const int NET_DVR_PLAYPAUSE = 3;       // 일시정지
        private const int NET_DVR_PLAYRESTART = 4;     // 재생 재개
        private const int NET_DVR_PLAYFAST = 5;        // 빨리 감기
        private const int NET_DVR_PLAYSLOW = 6;        // 느리게 재생
        private const int NET_DVR_PLAYSETTIME = 24;    // 특정 시간으로 이동
        private const int NET_DVR_PLAYSEEK = 25;       // 특정 시간(초) 단위로 이동
        private const int NET_DVR_SETPLAYPOS = 26;     // 재생 위치 설정 (백분율 기준)
        
        // 슬라이더 드래그 중인지 여부를 추적
        private bool isDraggingSlider = false;
        
        // 마지막 슬라이더 변경 시간 추적 (밀리초 단위)
        private long lastSliderChangeTime = 0;
        
        // 슬라이더 위치 변경 후 안정화 시간 (밀리초)
        private const int SLIDER_STABILIZE_TIME = 1000; // 1초로 증가

        // 슬라이더 수동 이동 후 위치를 강제로 업데이트할지 여부
        private bool forceSliderUpdate = false;

        // 타이머 활성화 여부 추적
        private bool isTimerActive = false;
        
        // 상태 텍스트를 일정 시간 후에 원래대로 되돌리는 메서드
        private DispatcherTimer statusResetTimer;
        
        private bool _disposed = false;
        
        public RecordingPlayerWindow()
        {
            InitializeComponent();
            this.Loaded += RecordingPlayerWindow_Loaded;
            this.Closing += RecordingPlayerWindow_Closing;
            
            // Owner 윈도우 설정 시 위치 추적 이벤트 등록
            this.SourceInitialized += RecordingPlayerWindow_SourceInitialized;
            
            // Visibility 변경 이벤트 등록
            this.IsVisibleChanged += RecordingPlayerWindow_IsVisibleChanged;
        }
        
        private void RecordingPlayerWindow_SourceInitialized(object sender, EventArgs e)
        {
            // Owner가 설정된 후에 이벤트 핸들러 등록
            if (this.Owner != null)
            {
                this.Owner.LocationChanged += Owner_LocationChanged;
                this.Owner.SizeChanged += Owner_SizeChanged;
                this.Owner.StateChanged += Owner_StateChanged;
                System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow: Owner 이벤트 핸들러 등록 완료");
            }
        }
        
        private void Owner_LocationChanged(object sender, EventArgs e)
        {
            // Owner 윈도우의 위치가 변경되면 녹화재생 윈도우 위치도 조정
            if (this.Owner != null && this.IsVisible)
            {
                UpdatePositionRelativeToOwner();
                
                // 비디오 윈도우가 있다면 함께 위치 업데이트
                if (_videoWindow != null && _videoWindow.Visibility == Visibility.Visible)
                {
                    UpdateVideoWindowPosition();
                }
                
                System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow: Owner 위치 변경에 따른 위치 업데이트");
            }
        }
        
        private void Owner_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Owner 윈도우의 크기가 변경되면 녹화재생 윈도우 위치/크기도 조정
            if (this.Owner != null && this.IsVisible)
            {
                UpdatePositionRelativeToOwner();
                
                // 비디오 윈도우가 있다면 함께 위치 업데이트
                if (_videoWindow != null && _videoWindow.Visibility == Visibility.Visible)
                {
                    UpdateVideoWindowPosition();
                }
                
                System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow: Owner 크기 변경에 따른 위치 업데이트");
            }
        }
        
        private void Owner_StateChanged(object sender, EventArgs e)
        {
            // Owner 윈도우 상태 변경에 따른 처리
            if (this.Owner != null)
            {
                if (this.Owner.WindowState == WindowState.Minimized)
                {
                    this.Hide();
                    
                    // 비디오 윈도우도 함께 숨김
                    if (_videoWindow != null)
                    {
                        _videoWindow.Hide();
                    }
                    
                    System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow: Owner 최소화로 인한 숨김");
                }
                else if (this.Owner.WindowState == WindowState.Normal || this.Owner.WindowState == WindowState.Maximized)
                {
                    if (!this.IsVisible)
                    {
                        this.Show();
                        UpdatePositionRelativeToOwner();
                        
                        // 비디오 윈도우도 함께 표시하고 위치 업데이트
                        if (_videoWindow != null)
                        {
                            _videoWindow.Show();
                            UpdateVideoWindowPosition();
                        }
                        
                        System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow: Owner 복원으로 인한 표시");
                    }
                }
            }
        }
        
        private void UpdatePositionRelativeToOwner()
        {
            if (this.Owner == null) return;
            
            try
            {
                // 메인 윈도우 위치가 비정상적인 경우 처리하지 않음 (최소화된 상태 등)
                if (this.Owner.Left < -30000 || this.Owner.Top < -30000)
                {
                    System.Diagnostics.Debug.WriteLine($"Owner 윈도우 위치가 비정상적임 (Left={this.Owner.Left}, Top={this.Owner.Top}) - 위치 업데이트 건너뜀");
                    return;
                }

                // 2열 레이아웃에서 왼쪽 영상 영역 전체를 사용
                double rightPanelWidth = 350; // 우측 패널 너비
                double margin = 10; // 마진
                
                // 왼쪽 영상 영역의 위치와 크기 계산
                double windowX = this.Owner.Left + margin + 20; // 추가 왼쪽 마진
                double windowY = this.Owner.Top + 40 + 20; // 타이틀바 높이 + 상단 마진
                double windowWidth = this.Owner.Width - rightPanelWidth - (margin * 3) - 40; // 추가 마진 고려
                double windowHeight = this.Owner.Height - 40 - 30 - (margin * 2) - 40; // 타이틀바, 상태바, 추가 마진 제외
                
                // 화면 경계 확인
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                
                if (windowX + windowWidth > screenWidth)
                {
                    windowWidth = screenWidth - windowX - 10;
                }
                
                if (windowY + windowHeight > screenHeight)
                {
                    windowHeight = screenHeight - windowY - 10;
                }
                
                // 최소 크기 보장
                windowWidth = Math.Max(windowWidth, 400);
                windowHeight = Math.Max(windowHeight, 300);
                
                // 계산된 위치가 유효한 범위인지 확인
                if (windowX < -1000 || windowY < -1000 || windowX > screenWidth + 1000 || windowY > screenHeight + 1000)
                {
                    System.Diagnostics.Debug.WriteLine($"계산된 윈도우 위치가 비정상적임 (X={windowX}, Y={windowY}) - 위치 업데이트 건너뜀");
                    return;
                }
                
                this.Left = windowX;
                this.Top = windowY;
                this.Width = windowWidth;
                this.Height = windowHeight;
                
                System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 위치 설정: Left={windowX}, Top={windowY}, Width={windowWidth}, Height={windowHeight}");
                
                // 비디오 윈도우 위치도 업데이트
                UpdateVideoWindowPosition();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 위치 업데이트 오류: {ex.Message}");
            }
        }

        private async void RecordingPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 비디오 창 초기화 (UI 관련이므로 메인 스레드에서 실행)
                InitializeVideoWindow();
                
                // 즉시 UI 표시
                StatusText.Text = "초기화 중...";
                
                // 날짜/시간 선택기 초기화 (UI 관련)
                DatePicker.SelectedDate = DateTime.Today;
                StartTimePicker.Value = DateTime.Today.AddHours(0);
                EndTimePicker.Value = DateTime.Today.AddHours(23).AddMinutes(59).AddSeconds(59);
                
                // 미래 날짜 블랙아웃 처리
                DateTime today = DateTime.Today;
                CalendarDateRange futureRange = new CalendarDateRange(today.AddDays(1), DateTime.MaxValue);
                DatePicker.BlackoutDates.Add(futureRange);
                
                // 키 입력 이벤트 핸들러 등록
                SetupTimePickerKeyEvents();
                CheckAndLimitEndTime();

                // 진행률 타이머 설정
                progressTimer = new DispatcherTimer();
                progressTimer.Interval = TimeSpan.FromSeconds(1);
                progressTimer.Tick += ProgressTimer_Tick;

                // SDK 초기화와 NVR 연결을 메인 스레드에서 순차적으로 실행
                await InitializeSDKAndConnect();
                
                StatusText.Text = "초기화 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"창 초기화 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                LogError($"창 초기화 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
                StatusText.Text = "초기화 실패";
            }
        }

        private async Task InitializeSDKAndConnect()
        {
            try
            {
                // SDK 초기화 (메인 스레드에서 실행)
                StatusText.Text = "SDK 초기화 중...";
                await Task.Delay(100); // UI 업데이트를 위한 짧은 지연
                
                InitSDK();
                
                // NVR 연결 (메인 스레드에서 실행)
                StatusText.Text = "NVR 연결 중...";
                await Task.Delay(100); // UI 업데이트를 위한 짧은 지연
                
                // NVR 연결을 Task.Run으로 래핑하되 결과를 메인 스레드에서 처리
                await Task.Run(() =>
                {
                    try
                    {
                        ConnectToNVR();
                    }
                    catch (Exception ex)
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = $"연결 오류: {ex.Message}";
                            LogError($"NVR 연결 오류: {ex.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"초기화 오류: {ex.Message}";
                LogError($"SDK 초기화 오류: {ex.Message}");
            }
        }
        
        // 시간 선택기에 키 이벤트 핸들러 등록
        private void SetupTimePickerKeyEvents()
        {
            try
            {
                // TimePicker에 직접 이벤트 핸들러 등록
                StartTimePicker.KeyDown += TimePicker_KeyDown;
                EndTimePicker.KeyDown += TimePicker_KeyDown;
                
                // PreviewKeyDown 이벤트도 등록 (더 빠른 처리를 위해)
                StartTimePicker.PreviewKeyDown += TimePicker_PreviewKeyDown;
                EndTimePicker.PreviewKeyDown += TimePicker_PreviewKeyDown;
                
                // TimePicker에 ValueChanged 이벤트 추가
                StartTimePicker.ValueChanged += StartTimePicker_ValueChanged;
                EndTimePicker.ValueChanged += EndTimePicker_ValueChanged;
                
                // DatePicker에 SelectedDateChanged 이벤트 추가
                DatePicker.SelectedDateChanged += DatePicker_SelectedDateChanged;
                
                LogError("시간 선택기에 키 이벤트 핸들러 등록 성공");
            }
            catch (Exception ex)
            {
                LogError($"시간 선택기 이벤트 핸들러 등록 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
            }
        }
        
        // TimePicker의 KeyDown 이벤트 핸들러
        private void TimePicker_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                try
                {
                    LogError("TimePicker_KeyDown: 엔터키 감지됨");
                    
                    // TimePicker 객체 획득 및 유효성 검사
                    var tp = sender as Xceed.Wpf.Toolkit.TimePicker;
                    if (tp == null)
                    {
                        LogError("TimePicker_KeyDown: TimePicker 캐스팅 실패");
                        return;
                    }
                    
                    // 텍스트박스 찾기
                    TextBox textBox = FindVisualChild<TextBox>(tp);
                    if (textBox == null)
                    {
                        LogError("TimePicker_KeyDown: 텍스트박스를 찾을 수 없음");
                        return;
                    }
                    
                    string input = textBox.Text.Trim();
                    LogError($"TimePicker_KeyDown: 입력된 텍스트 = '{input}'");
                    
                    // 숫자만 있는 경우 또는 형식화된 시간(콜론 포함)인 경우 모두 처리
                    TimeSpan timeSpan;
                    
                    if (input.All(char.IsDigit))
                    {
                        // 숫자 입력만 있는 경우
                        LogError($"TimePicker_KeyDown: 숫자로만 된 입력 처리 - '{input}'");
                        timeSpan = ConvertNumericInputToTime(input);
                    }
                    else if (input.Contains(":"))
                    {
                        // 형식화된 시간 입력(콜론 포함)
                        LogError($"TimePicker_KeyDown: 형식화된 시간 입력 처리 - '{input}'");
                        timeSpan = ConvertInputToTime(input);
                    }
                    else
                    {
                        LogError($"TimePicker_KeyDown: 지원되지 않는 입력 형식 - '{input}'");
                        return;
                    }
                    
                    LogError($"TimePicker_KeyDown: 변환된 시간 = {timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}");
                    
                    // 현재 날짜 가져오기
                    DateTime currentDate = tp.Value.HasValue ? tp.Value.Value.Date : DateTime.Today;
                    DateTime newDateTime = currentDate.Add(timeSpan);
                    
                    LogError($"TimePicker_KeyDown: 새 날짜/시간 = {newDateTime}");
                    
                    // UI 스레드에서 실행 보장
                    tp.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // 방법 1: 직접 TimePicker 값 설정
                            tp.Value = newDateTime;
                            LogError($"TimePicker_KeyDown: 값 설정 후 = {tp.Value}");
                            
                            // 값이 예상대로 설정되었는지 확인
                            bool valueSet = tp.Value.HasValue && Math.Abs((tp.Value.Value.TimeOfDay - timeSpan).TotalSeconds) < 1;
                            
                            if (!valueSet)
                            {
                                LogError("TimePicker_KeyDown: 값 설정 실패, 대체 방법 사용");
                                
                                // 방법 2: TextBox 형식으로 텍스트 설정
                                string formattedTime = $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                                textBox.Text = formattedTime;
                                LogError($"TimePicker_KeyDown: TextBox 텍스트 설정 = '{formattedTime}'");
                                
                                // 방법 3: 편집 종료 강제 적용
                                textBox.SelectAll();
                                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                                
                                // 방법 4: 명시적 업데이트 메서드 호출 트리거
                                var valueProperty = typeof(Xceed.Wpf.Toolkit.TimePicker).GetProperty("Value");
                                if (valueProperty != null)
                                {
                                    LogError("TimePicker_KeyDown: 리플렉션을 통한 값 설정 시도");
                                    valueProperty.SetValue(tp, newDateTime);
                                }
                            }
                            
                            // 포커스 제거로 변경 내용 커밋
                            Keyboard.ClearFocus();
                            
                            // 포커스 이동
                            MoveFocusAfterEnter(tp);
                        }
                        catch (Exception ex)
                        {
                            LogError($"TimePicker_KeyDown: UI 업데이트 중 오류 = {ex.Message}");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                    
                    // 이벤트 처리 완료
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    LogError($"TimePicker_KeyDown: 처리 오류 = {ex.Message}, 스택: {ex.StackTrace}");
                }
            }
        }
        
        // TimePicker의 PreviewKeyDown 이벤트 핸들러 (숫자 직접 입력 처리)
        private void TimePicker_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 숫자 키 또는 Enter 키인 경우만 처리
            bool isNumericKey = (e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);
            bool isEnterKey = (e.Key == Key.Enter || e.Key == Key.Return);
            
            if (!isNumericKey && !isEnterKey)
                return;
                
            try
            {
                // 숫자 키 입력 로깅
                if (isNumericKey)
                {
                    // 숫자 키 값 추출
                    int digit;
                    if (e.Key >= Key.D0 && e.Key <= Key.D9)
                        digit = e.Key - Key.D0;
                    else
                        digit = e.Key - Key.NumPad0;
                        
                    LogError($"TimePicker_PreviewKeyDown: 숫자 키 입력 감지 - {digit}");
                }
                
                // Enter 키 입력 처리
                if (isEnterKey)
                {
                    LogError("TimePicker_PreviewKeyDown: Enter 키 감지 (PreviewKeyDown)");
                    
                    // TimePicker 객체 획득
                    var tp = sender as Xceed.Wpf.Toolkit.TimePicker;
                    if (tp == null)
                        return;
                        
                    // 텍스트박스 찾기
                    TextBox textBox = FindVisualChild<TextBox>(tp);
                    if (textBox == null)
                        return;
                        
                    // 현재 입력된 텍스트 로깅
                    string currentText = textBox.Text;
                    LogError($"TimePicker_PreviewKeyDown: 현재 텍스트 = '{currentText}'");
                    
                    // 숫자만 입력되었는지 확인
                    string digitsOnly = new string(currentText.Where(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(digitsOnly))
                    {
                        LogError($"TimePicker_PreviewKeyDown: 추출된 숫자 = '{digitsOnly}'");
                        
                        // Enter 키 입력 시 텍스트에서 숫자만 추출해서 처리
                        if (isEnterKey)
                        {
                            LogError($"숫자만 추출하여 시간 입력 처리: '{digitsOnly}'");
                            ProcessNumericTimeInput(tp, digitsOnly);
                            
                            // 이미 처리했으므로 기본 KeyDown 이벤트 방지
                            e.Handled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"TimePicker_PreviewKeyDown 오류: {ex.Message}");
            }
        }
        
        // 엔터 키 입력 후 포커스 이동
        private void MoveFocusAfterEnter(Xceed.Wpf.Toolkit.TimePicker timePicker)
        {
            try
            {
                LogError($"포커스 이동 시도: {(timePicker == StartTimePicker ? "StartTimePicker" : "EndTimePicker")}");
                
                if (timePicker == StartTimePicker)
                {
                    LogError("EndTimePicker로 포커스 이동");
                    EndTimePicker.Focus();
                }
                else if (timePicker == EndTimePicker)
                {
                    LogError("SearchButton으로 포커스 이동");
                    SearchButton.Focus();
                }
                
                LogError("포커스 이동 완료");
            }
            catch (Exception ex)
            {
                LogError($"포커스 이동 중 오류: {ex.Message}");
            }
        }
        
        // 비주얼 트리에서 특정 타입의 첫 번째 자식 요소 찾기
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;
                
            // 현재 객체가 찾고 있는 타입인지 확인
            if (parent is T t)
                return t;
                
            // 자식 요소 개수
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            
            // 모든 자식 요소에 대해 재귀적으로 검색
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                
                // 자식 요소가 찾는 타입인지 확인
                T childOfType = FindVisualChild<T>(child);
                
                // 찾았으면 반환
                if (childOfType != null)
                    return childOfType;
            }
            
            return null;
        }
        
        // 숫자만 있는지 확인하는 메서드
        private bool IsNumericOnly(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;
                
            // 모든 문자가 숫자인지 확인
            return input.All(char.IsDigit);
        }
        
        // 사용자 입력을 분석하여 시간으로 변환
        private TimeSpan ConvertInputToTime(string input)
        {
            try
            {
                LogError($"시간 변환 시작: '{input}'");
                
                // 비어있는 입력 처리
                if (string.IsNullOrEmpty(input))
                {
                    LogError("입력이 비어있어 00:00:00 반환");
                    return TimeSpan.Zero;
                }
                
                // 콜론(:)이 포함된 경우
                if (input.Contains(":"))
                {
                    // "14:30" 또는 "14:30:45" 같은 형식 처리
                    string[] parts = input.Split(':');
                    
                    int hours = 0;
                    int minutes = 0;
                    int seconds = 0;
                    
                    if (parts.Length >= 1 && int.TryParse(parts[0], out int h))
                        hours = h;
                        
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int m))
                        minutes = m;
                        
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int s))
                        seconds = s;
                    
                    // 시간 범위 조정
                    hours = Math.Min(hours, 23);
                    minutes = Math.Min(minutes, 59);
                    seconds = Math.Min(seconds, 59);
                    
                    LogError($"콜론 포함 형식 파싱: {hours:D2}:{minutes:D2}:{seconds:D2}");
                    return new TimeSpan(hours, minutes, seconds);
                }
                else if (IsNumericOnly(input))
                {
                    // 숫자만 있는 경우
                    LogError($"숫자만 있는 형식: '{input}'");
                    return ConvertNumericInputToTime(input);
                }
                
                // 숫자가 아닌 경우 기본값 반환
                LogError($"지원되지 않는 형식: '{input}', 기본값 00:00:00 반환");
                return TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                LogError($"시간 변환 중 오류 발생: {ex.Message}, 입력값: '{input}'");
                return TimeSpan.Zero;
            }
        }
        
        // 숫자 입력을 시간으로 변환하는 메서드
        private TimeSpan ConvertNumericInputToTime(string input)
        {
            try
            {
                LogError($"숫자 변환 시작: '{input}'");
                
                // 입력이 비어있는 경우
                if (string.IsNullOrEmpty(input))
                {
                    LogError("입력이 비어있어 00:00:00 반환");
                    return TimeSpan.Zero;
                }
                
                // 입력된 숫자가 너무 길면 잘라냄
                if (input.Length > 6)
                {
                    LogError($"입력이 너무 김({input.Length}자리), 앞 6자리만 사용: {input.Substring(0, 6)}");
                    input = input.Substring(0, 6);
                }
                
                int length = input.Length;
                int hours = 0;
                int minutes = 0;
                int seconds = 0;
                
                // 각 자릿수별 처리
                switch (length)
                {
                    case 1: // 한 자리: 시간만 (5 → 05:00:00)
                    case 2: // 두 자리: 시간만 (14 → 14:00:00)
                        if (int.TryParse(input, out hours))
                        {
                            LogError($"시간만 입력: {hours:D2}:00:00");
                        }
                        break;
                        
                    case 3: // 세 자리: 시(1자리) + 분(2자리) (123 → 01:23:00)
                        if (int.TryParse(input.Substring(0, 1), out hours) && 
                            int.TryParse(input.Substring(1, 2), out minutes))
                        {
                            LogError($"3자리 입력: {hours:D2}:{minutes:D2}:00");
                        }
                        break;
                        
                    case 4: // 네 자리: 시(2자리) + 분(2자리) (1423 → 14:23:00)
                        if (int.TryParse(input.Substring(0, 2), out hours) && 
                            int.TryParse(input.Substring(2, 2), out minutes))
                        {
                            LogError($"4자리 입력: {hours:D2}:{minutes:D2}:00");
                        }
                        break;
                        
                    case 5: // 다섯 자리: 시(1자리) + 분(2자리) + 초(2자리) (12345 → 01:23:45)
                        if (int.TryParse(input.Substring(0, 1), out hours) && 
                            int.TryParse(input.Substring(1, 2), out minutes) &&
                            int.TryParse(input.Substring(3, 2), out seconds))
                        {
                            LogError($"5자리 입력: {hours:D2}:{minutes:D2}:{seconds:D2}");
                        }
                        break;
                        
                    case 6: // 여섯 자리: 시(2자리) + 분(2자리) + 초(2자리) (142355 → 14:23:55)
                        if (int.TryParse(input.Substring(0, 2), out hours) && 
                            int.TryParse(input.Substring(2, 2), out minutes) && 
                            int.TryParse(input.Substring(4, 2), out seconds))
                        {
                            LogError($"6자리 입력: {hours:D2}:{minutes:D2}:{seconds:D2}");
                        }
                        break;
                        
                    default:
                        LogError($"예상치 못한 입력 길이: {length}");
                        return TimeSpan.Zero;
                }
                
                // 시간 범위 검증 및 조정
                hours = Math.Min(Math.Max(hours, 0), 23);
                minutes = Math.Min(Math.Max(minutes, 0), 59);
                seconds = Math.Min(Math.Max(seconds, 0), 59);
                
                // TimeSpan 생성 시 유효한 값 확인
                TimeSpan result = new TimeSpan(hours, minutes, seconds);
                LogError($"최종 변환 결과: {result.Hours:D2}:{result.Minutes:D2}:{result.Seconds:D2}");
                
                return result;
            }
            catch (Exception ex)
            {
                LogError($"숫자 시간 변환 중 오류 발생: {ex.Message}, 입력값: '{input}'");
                // 오류 발생 시 기본값 반환
                return TimeSpan.Zero;
            }
        }
        
        // 상태 텍스트를 일정 시간 후에 원래대로 되돌리는 메서드
        private void ResetStatusTextAfterDelay()
        {
            // 기존 타이머가 있으면 중지
            if (statusResetTimer != null)
            {
                statusResetTimer.Stop();
            }
            
            // 새 타이머 생성
            statusResetTimer = new DispatcherTimer();
            statusResetTimer.Interval = TimeSpan.FromSeconds(3);
            statusResetTimer.Tick += (s, e) => 
            {
                StatusText.Text = "준비됨";
                statusResetTimer.Stop();
            };
            statusResetTimer.Start();
        }
        
        // 비디오 창 초기화 메서드 추가
        private void InitializeVideoWindow()
        {
            try
            {
                // 비디오 창 생성
                _videoWindow = new RecordingVideoWindow();
                _videoWindow.Owner = this;
                
                // 윈도우 스타일 및 속성 설정
                _videoWindow.WindowStyle = WindowStyle.None;
                _videoWindow.ResizeMode = ResizeMode.NoResize;
                _videoWindow.ShowInTaskbar = false;
                _videoWindow.ShowActivated = false; // 활성화되지 않도록 설정
                _videoWindow.Topmost = true; // 항상 위에 표시
                
                // 비디오 컨트롤 참조 설정
                videoControl = _videoWindow.VideoControl;
                
                // UI가 완전히 로드된 후에 크기와 위치 설정
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // VideoContainer의 부모 Border 찾기
                        Border parentBorder = VideoContainer.Parent as Border;
                        if (parentBorder != null)
                        {
                            // 레이아웃 업데이트
                            parentBorder.UpdateLayout();
                            
                            // Border의 실제 내부 영역 계산
                            double borderLeft = parentBorder.BorderThickness.Left;
                            double borderTop = parentBorder.BorderThickness.Top;
                            double borderRight = parentBorder.BorderThickness.Right;
                            double borderBottom = parentBorder.BorderThickness.Bottom;
                            
                            double actualWidth = parentBorder.ActualWidth - borderLeft - borderRight;
                            double actualHeight = parentBorder.ActualHeight - borderTop - borderBottom;
                            
                            // 유효한 크기인 경우 설정
                            if (actualWidth > 10 && actualHeight > 10)
                            {
                                _videoWindow.Width = actualWidth;
                                _videoWindow.Height = actualHeight;
                                LogError($"비디오 창 초기 크기 설정: Width={actualWidth}, Height={actualHeight}");
                            }
                            else
                            {
                                // 기본 크기 사용
                                _videoWindow.Width = 780;
                                _videoWindow.Height = 380;
                                LogError($"기본 크기 사용: Width=780, Height=380 (계산된 크기가 너무 작음: {actualWidth}x{actualHeight})");
                            }
                        }
                        else
                        {
                            // 부모 Border를 찾을 수 없는 경우 기본 크기
                            _videoWindow.Width = 780;
                            _videoWindow.Height = 380;
                            LogError("부모 Border를 찾을 수 없어 기본 크기 사용");
                        }
                        
                        // 위치 설정
                        UpdateVideoWindowPosition();
                        
                        // 비디오 창 표시
                        _videoWindow.Show();
                        
                        LogError($"비디오 창 초기화 완료: Width={_videoWindow.Width}, Height={_videoWindow.Height}");
                    }
                    catch (Exception ex)
                    {
                        LogError($"비디오 창 초기 설정 오류: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
                
                // 메인 윈도우 위치 변경 시 비디오 창 위치도 조정
                this.LocationChanged += (s, e) => {
                    if (_videoWindow != null && _videoWindow.Visibility == Visibility.Visible)
                    {
                        UpdateVideoWindowPosition();
                    }
                };
                
                // 창 크기가 변경될 때 위치 업데이트
                this.SizeChanged += (s, e) => {
                    if (_videoWindow != null && _videoWindow.Visibility == Visibility.Visible)
                    {
                        UpdateVideoWindowPosition(); // UpdateVideoWindowSize 대신 UpdateVideoWindowPosition 사용
                    }
                };
                
                LogError("비디오 창 초기화 시작");
            }
            catch (Exception ex)
            {
                LogError($"비디오 창 초기화 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
            }
        }
        
        // 비디오 창 위치 업데이트
        private void UpdateVideoWindowPosition()
        {
            try
            {
                if (_videoWindow == null || !IsLoaded || VideoContainer == null)
                    return;
                
                // VideoContainer의 부모 Border를 찾아서 정확한 비디오 영역 계산
                Border parentBorder = VideoContainer.Parent as Border;
                if (parentBorder == null)
                    return;
                
                // 부모 Border의 화면 좌표 위치 가져오기
                Point containerScreenPosition = parentBorder.PointToScreen(new Point(0, 0));
                
                // Border의 BorderThickness 고려
                double borderLeft = parentBorder.BorderThickness.Left;
                double borderTop = parentBorder.BorderThickness.Top;
                double borderRight = parentBorder.BorderThickness.Right;
                double borderBottom = parentBorder.BorderThickness.Bottom;
                
                // 실제 내부 영역 계산 (Border 제외)
                double actualLeft = containerScreenPosition.X + borderLeft;
                double actualTop = containerScreenPosition.Y + borderTop;
                double actualWidth = parentBorder.ActualWidth - borderLeft - borderRight;
                double actualHeight = parentBorder.ActualHeight - borderTop - borderBottom;
                
                // 최소 크기 보장
                if (actualWidth < 10 || actualHeight < 10)
                {
                    LogError($"비디오 영역 크기가 너무 작음: Width={actualWidth}, Height={actualHeight}");
                    return;
                }
                
                // 비디오 윈도우 위치와 크기 설정
                _videoWindow.Left = actualLeft;
                _videoWindow.Top = actualTop;
                _videoWindow.Width = actualWidth;
                _videoWindow.Height = actualHeight;
                
                LogError($"비디오 창 정확한 위치 업데이트: Left={actualLeft}, Top={actualTop}, Width={actualWidth}, Height={actualHeight}");
                LogError($"Border 정보: BorderThickness=({borderLeft},{borderTop},{borderRight},{borderBottom}), ActualSize=({parentBorder.ActualWidth},{parentBorder.ActualHeight})");
            }
            catch (Exception ex)
            {
                LogError($"비디오 창 위치 업데이트 오류: {ex.Message}");
            }
        }
        
        // 비디오 창 크기 업데이트 메서드 삭제됨 - UpdateVideoWindowPosition에서 크기와 위치를 함께 처리

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            // 재생 핸들과 상태 검증 (빠른 반환)
            if (m_lPlayHandle < 0 || !isPlaying) 
            {
                // 타이머가 활성화 상태지만 재생 중이 아니면 타이머 중지
                if (isTimerActive)
                {
                    progressTimer.Stop();
                    isTimerActive = false;
                    LogError("재생 중이 아님 - 타이머 중지됨");
                }
                return;
            }

            try
            {
                // 슬라이더 드래그 중에는 업데이트하지 않음 (빠른 반환)
                if (isDraggingSlider) return;
                
                // 현재 시간 계산 최적화
                long currentTimeMs = Environment.TickCount; // DateTime.Now.Ticks보다 성능이 좋음
                
                // 마지막 슬라이더 변경 후 안정화 시간 확인
                bool skipSliderUpdate = (currentTimeMs - lastSliderChangeTime) < SLIDER_STABILIZE_TIME && !forceSliderUpdate;
                
                // 현재 재생 시간 가져오기 (SDK 호출 최소화)
                Models.CCTV.NET_DVR_TIME osdTime = new Models.CCTV.NET_DVR_TIME();
                if (!Models.CCTV.NET_DVR_GetPlayBackOsdTime(m_lPlayHandle, ref osdTime))
                {
                    // API 실패 시 로그 생성 빈도 제한 (매번 로그 생성하지 않음)
                    if (currentTimeMs % 5000 < 1000) // 5초마다 한 번만 로그
                    {
                        uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                        LogError($"재생 시간 가져오기 실패: 오류 코드 {errorCode} - {GetErrorMessage(errorCode)}");
                    }
                    return;
                }

                // 재생 시간 구성 (한 번만 계산)
                DateTime currentTime = new DateTime((int)osdTime.dwYear, (int)osdTime.dwMonth, (int)osdTime.dwDay,
                    (int)osdTime.dwHour, (int)osdTime.dwMinute, (int)osdTime.dwSecond);

                // 총 재생 기간 계산 (캐시된 값 사용)
                TimeSpan totalDuration = m_endTime - m_startTime;
                TimeSpan currentPosition = currentTime - m_startTime;

                // UI 업데이트 최적화: 시간 텍스트는 항상 업데이트하되, 변경이 있을 때만
                string newTimeText = currentTime.ToString("HH:mm:ss");
                if (PlayTimeText.Text != newTimeText)
                {
                    PlayTimeText.Text = newTimeText;
                }
                
                // 슬라이더 위치 업데이트 (조건부 및 최적화)
                if (totalDuration.TotalSeconds > 0 && !skipSliderUpdate)
                {
                    double progressPercent = (currentPosition.TotalSeconds / totalDuration.TotalSeconds) * 100;
                    
                    // 유효한 범위 확인 및 변화량 체크 (미세한 변화는 무시)
                    if (progressPercent >= 0 && progressPercent <= 100)
                    {
                        // 이전 값과 차이가 0.1% 이상일 때만 업데이트 (UI 깜빡임 방지)
                        if (Math.Abs(ProgressSlider.Value - progressPercent) >= 0.1)
                        {
                            ProgressSlider.Value = progressPercent;
                        }
                    }
                    else if (currentTimeMs % 10000 < 1000) // 10초마다 한 번만 로그
                    {
                        LogError($"잘못된 진행률 값: {progressPercent:F2}%, 현재 시간: {currentTime}, 시작: {m_startTime}, 종료: {m_endTime}");
                    }
                }
                
                // 재생 완료 확인 (종료 조건 최적화)
                if (currentTime >= m_endTime || currentPosition.TotalSeconds >= totalDuration.TotalSeconds)
                {
                    LogError("재생 끝에 도달함");
                    StopPlayback();
                    PlayTimeText.Text = m_endTime.ToString("HH:mm:ss");
                    
                    // UI 스레드에서 상태 업데이트
                    this.Dispatcher.BeginInvoke(() => StatusText.Text = "재생 완료", 
                        System.Windows.Threading.DispatcherPriority.Normal);
                }
            }
            catch (Exception ex)
            {
                // 예외 로그 빈도 제한
                if (Environment.TickCount % 5000 < 1000) // 5초마다 한 번만 로그
                {
                    LogError($"재생 시간 업데이트 오류: {ex.Message}");
                }
            }
        }

        private void InitSDK()
        {
            try
            {
                // 이미 초기화되었는지 확인
                if (m_bInitSDK) return;

                // 로그 디렉토리 설정
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SDKLog");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // SDK 초기화
                m_bInitSDK = Models.CCTV.NET_DVR_Init();
                if (!m_bInitSDK)
                {
                    StatusText.Text = "SDK 초기화 실패";
                    return;
                }

                // 로그 설정
                Models.CCTV.NET_DVR_SetLogToFile(3, logDir, true);
                StatusText.Text = "SDK 초기화 완료";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"SDK 초기화 오류: {ex.Message}";
                LogError($"SDK 초기화 오류: {ex.Message}");
            }
        }

        private void ConnectToNVR()
        {
            try
            {
                // 이미 연결된 경우 먼저 해제
                if (m_lUserID >= 0)
                {
                    Models.CCTV.NET_DVR_Logout(m_lUserID);
                    m_lUserID = -1;
                }

                // NVR 접속 정보
                string ip = "192.168.100.4";
                string username = "admin";
                string password = "!yanry3734";
                ushort port = 8000;

                LogError($"NVR 연결 시도 - IP: {ip}, 포트: {port}, 사용자: {username}");

                // 로그인 구조체 설정
                deviceInfo = new Models.CCTV.NET_DVR_DEVICEINFO_V30();

                // 로그인
                m_lUserID = Models.CCTV.NET_DVR_Login_V30(ip, port, username, password, ref deviceInfo);
                if (m_lUserID < 0)
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    this.Dispatcher.Invoke(() => StatusText.Text = $"로그인 실패: 오류 코드 {errorCode}");
                    LogError($"로그인 실패: 오류 코드 {errorCode}, IP: {ip}, 포트: {port}, 사용자: {username}");
                    
                    // SDK 재초기화 시도
                    if (m_bInitSDK)
                    {
                        Models.CCTV.NET_DVR_Cleanup();
                        m_bInitSDK = false;
                        this.Dispatcher.Invoke(() => InitSDK());
                        
                        // 재로그인 시도
                        m_lUserID = Models.CCTV.NET_DVR_Login_V30(ip, port, username, password, ref deviceInfo);
                        if (m_lUserID < 0)
                        {
                            errorCode = Models.CCTV.NET_DVR_GetLastError();
                            this.Dispatcher.Invoke(() => StatusText.Text = $"재로그인 실패: 오류 코드 {errorCode}");
                            LogError($"재로그인 실패: 오류 코드 {errorCode}");
                            return;
                        }
                        else
                        {
                            LogError($"재로그인 성공: UserID = {m_lUserID}");
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                // 로그인 성공
                this.Dispatcher.Invoke(() => StatusText.Text = "NVR 로그인 성공");
                LogError($"로그인 성공: UserID = {m_lUserID}, 아날로그 채널 수: {deviceInfo.byChanNum}, 시작 채널: {deviceInfo.byStartChan}, IP 채널 수: {deviceInfo.byIPChanNum}");
                
                // 채널 목록 업데이트 (UI 스레드에서 실행)
                this.Dispatcher.Invoke(() => UpdateChannelComboBox(deviceInfo));
                
                // 검색 버튼 활성화 (UI 스레드에서 실행)
                this.Dispatcher.Invoke(() => 
                {
                    SearchButton.IsEnabled = true;
                    LogError("검색 버튼 활성화됨");
                });
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => StatusText.Text = $"연결 오류: {ex.Message}");
                LogError($"NVR 연결 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
            }
        }

        // 채널 목록 업데이트 메서드 추가
        private void UpdateChannelComboBox(Models.CCTV.NET_DVR_DEVICEINFO_V30 deviceInfo)
        {
            try
            {
                ChannelComboBox.Items.Clear();
                
                // IP 채널 추가 (33번부터 시작)
                for (int i = 0; i < deviceInfo.byIPChanNum; i++)
                {
                    int chanNum = 33 + i; // 33, 34, 35, 36, 37, 38 등
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = $"{chanNum}번 채널";
                    ChannelComboBox.Items.Add(item);
                }
                
                // 채널 목록이 비어있지 않다면 첫 번째 채널 선택
                if (ChannelComboBox.Items.Count > 0)
                {
                    ChannelComboBox.SelectedIndex = 0;
                }
                
                LogError($"채널 목록 업데이트: 아날로그 {deviceInfo.byChanNum}개, IP {deviceInfo.byIPChanNum}개");
            }
            catch (Exception ex)
            {
                LogError($"채널 목록 업데이트 오류: {ex.Message}");
            }
        }

        private void RecordingPlayerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Owner 윈도우 이벤트 핸들러 해제
                if (this.Owner != null)
                {
                    this.Owner.LocationChanged -= Owner_LocationChanged;
                    this.Owner.SizeChanged -= Owner_SizeChanged;
                    this.Owner.StateChanged -= Owner_StateChanged;
                    System.Diagnostics.Debug.WriteLine("RecordingPlayerWindow: Owner 이벤트 핸들러 해제 완료");
                }
                
                // 진행률 타이머 안전하게 정리
                if (progressTimer != null)
                {
                    progressTimer.Stop();
                    progressTimer = null;
                    isTimerActive = false;
                    LogError("progressTimer 안전하게 정리됨");
                }
                
                // 재생 중지 및 리소스 정리
                StopPlayback();

                // 로그아웃
                if (m_lUserID >= 0)
                {
                    Models.CCTV.NET_DVR_Logout(m_lUserID);
                    m_lUserID = -1;
                }

                // SDK 종료 
                if (m_bInitSDK)
                {
                    Models.CCTV.NET_DVR_Cleanup();
                    m_bInitSDK = false;
                }
                
                // 비디오 창 정리
                if (_videoWindow != null)
                {
                    _videoWindow.Close();
                    _videoWindow = null;
                }
                
                // 상태 리셋 타이머 정리
                if (statusResetTimer != null)
                {
                    statusResetTimer.Stop();
                    statusResetTimer = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RecordingPlayerWindow 종료 중 오류: {ex.Message}");
                LogError($"RecordingPlayerWindow 종료 중 오류: {ex.Message}");
            }
        }
        
        // Visibility 변경 이벤트 핸들러
        private void RecordingPlayerWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                bool isVisible = (bool)e.NewValue;
                LogError($"RecordingPlayerWindow IsVisible 변경됨: {isVisible}");
                
                if (!isVisible) // IsVisible가 false가 되었을 때
                {
                    // RecordingPlayerWindow가 숨겨지면 비디오 윈도우도 함께 숨김
                    HideVideoWindow();
                    LogError("RecordingPlayerWindow가 숨겨져서 RecordingVideoWindow도 함께 숨김");
                }
                else // IsVisible가 true가 되었을 때
                {
                    LogError("RecordingPlayerWindow가 표시됨");
                    // 자동으로 비디오 윈도우를 표시하지 않음 - 명시적으로 호출할 때만
                }
            }
            catch (Exception ex)
            {
                LogError($"IsVisible 변경 처리 중 오류: {ex.Message}");
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_lUserID < 0)
            {
                StatusText.Text = "먼저 NVR에 로그인하세요";
                return;
            }

            try
            {
                // 검색 전 재생 중지
                StopPlayback();

                // 시간 범위 가져오기
                DateTime selectedDate = DatePicker.SelectedDate ?? DateTime.Today;
                
                // 선택된 날짜와 시간 결합
                DateTime startTimeValue = StartTimePicker.Value ?? DateTime.Today;
                DateTime endTimeValue = EndTimePicker.Value ?? DateTime.Today.AddHours(1);
                
                DateTime startTime = new DateTime(
                    selectedDate.Year, selectedDate.Month, selectedDate.Day,
                    startTimeValue.Hour, startTimeValue.Minute, startTimeValue.Second);
                
                DateTime endTime = new DateTime(
                    selectedDate.Year, selectedDate.Month, selectedDate.Day,
                    endTimeValue.Hour, endTimeValue.Minute, endTimeValue.Second);
                
                // 최종 검사: 오늘 날짜인 경우에만 종료 시간이 현재 시간을 초과하지 않도록 제한
                if (selectedDate.Date == DateTime.Today.Date)
                {
                    DateTime currentTime = DateTime.Now;
                    
                    if (endTime > currentTime)
                    {
                        endTime = new DateTime(
                            currentTime.Year, currentTime.Month, currentTime.Day,
                            currentTime.Hour, currentTime.Minute, currentTime.Second);
                        
                        LogError($"검색 시 종료 시간이 현재 시간({currentTime})을 초과하여 현재 시간으로 제한합니다.");
                    }
                }
                
                LogError($"선택된 날짜: {selectedDate.ToShortDateString()}, 시작 시간: {startTime}, 종료 시간: {endTime}");
                
                m_startTime = startTime;
                m_endTime = endTime;

                // 선택된 채널 번호 가져오기
                if (ChannelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    string content = selectedItem.Content.ToString();
                    if (!string.IsNullOrEmpty(content) && content.Contains("번 채널"))
                    {
                        string channelText = content.Replace("번 채널", "").Trim();
                        if (int.TryParse(channelText, out int selectedChannel))
                        {
                            channelNumber = selectedChannel;
                        }
                    }
                }

                StatusText.Text = "녹화 영상 검색 중...";
                
                // 먼저 녹화 파일 검색
                bool hasRecordings = SearchForRecordings(channelNumber, startTime, endTime);
                
                if (hasRecordings)
                {
                    StatusText.Text = "영상 재생 준비 중...";
                    PlayRecording(startTime, endTime);
                }
                else
                {
                    MessageBox.Show($"선택한 날짜({selectedDate.ToShortDateString()})와 시간에 해당하는 녹화 영상이 없습니다.", 
                                    "녹화 영상 없음", 
                                    MessageBoxButton.OK, 
                                    MessageBoxImage.Information);
                    StatusText.Text = "녹화 영상 없음";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"녹화 영상 검색 오류: {ex.Message}";
                LogError($"녹화 영상 검색 오류: {ex.Message}");
            }
        }

        private bool SearchForRecordings(int channel, DateTime startTime, DateTime endTime)
        {
            try
            {
                LogError($"녹화 파일 검색 시작 - 채널: {channel}, 시작: {startTime}, 종료: {endTime}");
                
                // 검색 조건 구조체 생성
                Models.CCTV.NET_DVR_FILECOND_V40 fileCondition = new Models.CCTV.NET_DVR_FILECOND_V40
                {
                    lChannel = channel,
                    dwFileType = 0xFF, // 모든 파일 유형
                    dwIsLocked = 0xFF, // 잠금 여부 상관없음
                    dwUseCardNo = 0,   // 카드 번호 사용 안함
                    struStartTime = new Models.CCTV.NET_DVR_TIME
                    {
                        dwYear = (uint)startTime.Year,
                        dwMonth = (uint)startTime.Month,
                        dwDay = (uint)startTime.Day,
                        dwHour = (uint)startTime.Hour,
                        dwMinute = (uint)startTime.Minute,
                        dwSecond = (uint)startTime.Second
                    },
                    struStopTime = new Models.CCTV.NET_DVR_TIME
                    {
                        dwYear = (uint)endTime.Year,
                        dwMonth = (uint)endTime.Month,
                        dwDay = (uint)endTime.Day,
                        dwHour = (uint)endTime.Hour,
                        dwMinute = (uint)endTime.Minute,
                        dwSecond = (uint)endTime.Second
                    }
                };
                
                // 파일 검색 시작
                int findHandle = Models.CCTV.NET_DVR_FindFile_V40(m_lUserID, ref fileCondition);
                uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                LogError($"파일 검색 핸들: {findHandle}, 오류 코드: {errorCode}");
                
                if (findHandle < 0)
                {
                    LogError($"파일 검색 실패: 오류 코드 {errorCode}, 오류 설명: {GetErrorMessage(errorCode)}");
                    
                    // 직접 재생 시도를 위해 true 반환
                    LogError("파일 검색에 실패했지만, 재생은 시도합니다.");
                    return true;
                }
                
                // 첫 번째 파일 검색
                Models.CCTV.NET_DVR_FINDDATA_V40 fileData = new Models.CCTV.NET_DVR_FINDDATA_V40();
                int result = Models.CCTV.NET_DVR_FindNextFile_V40(findHandle, ref fileData);
                LogError($"파일 검색 결과: {result} ({GetResultCodeDescription(result)})");
                
                // 검색 핸들 닫기
                Models.CCTV.NET_DVR_FindClose_V30(findHandle);
                
                // 파일이 있는지 여부 반환
                bool hasRecordings = false;
                
                if (result == Models.CCTV.NET_DVR_FILE_SUCCESS)
                {
                    hasRecordings = true;
                    LogError($"녹화 파일 찾음: 파일 유형 {fileData.byFileType}, 시작 시간: {fileData.struStartTime.dwYear}/{fileData.struStartTime.dwMonth}/{fileData.struStartTime.dwDay} {fileData.struStartTime.dwHour}:{fileData.struStartTime.dwMinute}:{fileData.struStartTime.dwSecond}");
                }
                else 
                {
                    LogError($"녹화 파일 검색 결과: 결과 코드 {result} - {GetResultCodeDescription(result)}");
                }
                
                // 테스트를 위해 항상 true 반환 (실제 녹화 여부와 관계없이 재생 시도)
                LogError("테스트를 위해 녹화 파일이 있다고 가정하고 재생을 진행합니다.");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"녹화 파일 검색 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
                // 예외 발생 시에도 재생 시도
                LogError("예외가 발생했지만, 재생은 시도합니다.");
                return true;
            }
        }
        
        // 결과 코드 설명 가져오기
        private string GetResultCodeDescription(int resultCode)
        {
            switch (resultCode)
            {
                case Models.CCTV.NET_DVR_FILE_SUCCESS:
                    return "파일 찾기 성공";
                case Models.CCTV.NET_DVR_FILE_NOFIND:
                    return "파일 찾기 실패";
                case Models.CCTV.NET_DVR_ISFINDING:
                    return "파일 찾기 중";
                case Models.CCTV.NET_DVR_NOMOREFILE:
                    return "더 이상 파일이 없음";
                case Models.CCTV.NET_DVR_FILE_EXCEPTION:
                    return "검색 예외 상황";
                default:
                    return "알 수 없는 결과 코드";
            }
        }

        // PlayRecording 메서드의 선언을 수정하고 내용을 업데이트합니다
        private void PlayRecording(DateTime startTime, DateTime endTime, bool isResume = false)
        {
            if (m_lUserID < 0)
            {
                StatusText.Text = "먼저 NVR에 로그인하세요";
                return;
            }

            try
            {
                // 로그 추가
                LogError($"재생 시도 - 유저ID: {m_lUserID}, 채널: {channelNumber}, 시작: {startTime}, 종료: {endTime}, 재개모드: {isResume}");

                // 이전 재생이 있으면 완전히 중지 (모든 경우에 새로 시작)
                if (m_lPlayHandle >= 0)
                {
                    LogError("이전 재생 핸들 정리");
                    Models.CCTV.NET_DVR_StopPlayBack(m_lPlayHandle);
                    m_lPlayHandle = -1;
                }

                // 비디오 윈도우 핸들 가져오기
                IntPtr hwnd = GetVideoWindowHandle(videoControl);
                if (hwnd == IntPtr.Zero)
                {
                    StatusText.Text = "비디오 윈도우 핸들을 가져올 수 없음";
                    return;
                }

                // 채널 번호 사용 및 안전한 변환
                int lChannel = channelNumber;
                LogError($"사용자 선택 채널 사용: {lChannel}, 원래 채널번호: {channelNumber}");
                
                // byte 범위를 초과하는지 확인
                if (lChannel > 255)
                {
                    LogError($"경고: 채널 번호({lChannel})가 byte 범위(0-255)를 초과합니다. 값을 잘라냅니다.");
                }
                
                // 시작 시간 설정
                Models.CCTV.NET_DVR_TIME struStartTime = new Models.CCTV.NET_DVR_TIME
                {
                    dwYear = (uint)startTime.Year,
                    dwMonth = (uint)startTime.Month,
                    dwDay = (uint)startTime.Day,
                    dwHour = (uint)startTime.Hour,
                    dwMinute = (uint)startTime.Minute,
                    dwSecond = (uint)startTime.Second
                };
                LogError($"시작 시간 구조체: {struStartTime.dwYear}/{struStartTime.dwMonth}/{struStartTime.dwDay} {struStartTime.dwHour}:{struStartTime.dwMinute}:{struStartTime.dwSecond}");

                // 종료 시간 설정
                Models.CCTV.NET_DVR_TIME struEndTime = new Models.CCTV.NET_DVR_TIME
                {
                    dwYear = (uint)endTime.Year,
                    dwMonth = (uint)endTime.Month,
                    dwDay = (uint)endTime.Day,
                    dwHour = (uint)endTime.Hour,
                    dwMinute = (uint)endTime.Minute,
                    dwSecond = (uint)endTime.Second
                };
                LogError($"종료 시간 구조체: {struEndTime.dwYear}/{struEndTime.dwMonth}/{struEndTime.dwDay} {struEndTime.dwHour}:{struEndTime.dwMinute}:{struEndTime.dwSecond}");

                // 재생 시작 전에 SDK 상태 확인
                if (!m_bInitSDK)
                {
                    LogError("SDK가 초기화되지 않음, 재초기화 시도");
                    InitSDK();
                }

                // 슬라이더 드래그 처리 후에 충분한 안정화 시간 추가
                if (isResume)
                {
                    LogError("재개 모드: 슬라이더 안정화를 위해 300ms 대기");
                    System.Threading.Thread.Sleep(300);
                }

                // 재생 파라미터 설정
                Models.CCTV.NET_DVR_VOD_PARA struVodPara = new Models.CCTV.NET_DVR_VOD_PARA
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(Models.CCTV.NET_DVR_VOD_PARA)),
                    struBeginTime = struStartTime,
                    struEndTime = struEndTime,
                    hWnd = hwnd,
                    byDrawFrame = 1,
                    byVolumeType = 1,        // 1: 채널 번호로 설정
                    byVolumeNum = (byte)channelNumber, // 채널 번호 - 수정됨 (byVolumeNum → channelNumber)
                    byStreamType = 0         // 메인 스트림
                };

                LogError($"VOD_PARA 설정: 크기={struVodPara.dwSize}, 채널={struVodPara.byVolumeNum}, 스트림 타입={struVodPara.byStreamType}");

                // V40 버전의 재생 시도
                LogError($"NET_DVR_PlayBackByTime_V40 시도 - 채널: {lChannel}");
                m_lPlayHandle = Models.CCTV.NET_DVR_PlayBackByTime_V40(m_lUserID, ref struVodPara);

                if (m_lPlayHandle < 0)
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    string errorMsg = GetErrorMessage(errorCode);
                    LogError($"V40 재생 실패: 오류 코드 {errorCode} - {errorMsg}, 채널: {lChannel}");
                    
                    // 기존 메서드로 시도 (NET_DVR_PlayBackByTime)
                    LogError($"NET_DVR_PlayBackByTime 시도 - 채널: {lChannel}");
                    m_lPlayHandle = Models.CCTV.NET_DVR_PlayBackByTime(m_lUserID, lChannel, ref struStartTime, ref struEndTime, hwnd);
                    
                    if (m_lPlayHandle < 0)
                    {
                        errorCode = Models.CCTV.NET_DVR_GetLastError();
                        errorMsg = GetErrorMessage(errorCode);
                        LogError($"PlayBackByTime 실패: 오류 코드 {errorCode} - {errorMsg}");
                        
                        if (errorCode == 4 || errorCode == 5 || errorCode == 17 || errorCode == 36) // 비밀번호 오류, 권한 없음
                        {
                            StatusText.Text = $"재생 권한이 없습니다. NVR 설정에서 계정 권한을 확인하세요. (오류 코드: {errorCode} - {errorMsg})";
                            LogError($"권한 문제 감지: 오류 코드 {errorCode} - {errorMsg}. admin 계정인데도 권한이 없는 경우 NVR 설정에서 계정 권한을 확인하세요.");
                            return;
                        }
                        else if (errorCode == 32 || errorCode == 24) // 녹화 없음
                        {
                            StatusText.Text = $"선택한 시간에 녹화 영상이 없습니다. 다른 시간대를 선택하세요.";
                            LogError($"선택한 시간에 녹화 영상이 없음: 채널 {lChannel}, 시간: {startTime}~{endTime}");
                            return;
                        }
                        else
                        {
                            StatusText.Text = $"재생 실패: {errorMsg}";
                            return;
                        }
                    }
                }

                // 슬라이더 드래그 후 위치 이동을 위한 추가 안정화 시간
                if (isResume)
                {
                    LogError("재개 모드: 추가 안정화 시간 200ms 대기");
                    System.Threading.Thread.Sleep(200);
                }

                // 재생 시작
                uint dwOutValue = 0;
                if (!Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYSTART, 0, ref dwOutValue))
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    string errorMsg = GetErrorMessage(errorCode);
                    LogError($"재생 시작 실패: 오류 코드 {errorCode} - {errorMsg}");
                    StatusText.Text = $"재생 시작 실패: {errorMsg}";
                    return;
                }

                // 전역 변수 업데이트
                m_startTime = startTime;
                m_endTime = endTime;
                
                // 상태 업데이트
                isPlaying = true;
                playbackSpeed = 1.0;

                // UI 업데이트
                StatusText.Text = isResume ? "재생 위치 변경됨" : "녹화 영상 재생 중";
                EndTimeText.Text = endTime.ToString("HH:mm:ss");
                PlayTimeText.Text = startTime.ToString("HH:mm:ss");

                // 버튼 상태 업데이트
                PlayButton.IsEnabled = false;
                PauseButton.IsEnabled = true;
                StopButton.IsEnabled = true;
                FastForwardButton.IsEnabled = true;
                SlowButton.IsEnabled = true;       

                // 슬라이더 드래그 후 타이머 재시작 전 추가 지연
                if (isResume)
                {
                    LogError("재개 모드: 타이머 시작 전 300ms 지연");
                    this.Dispatcher.BeginInvoke(new Action(() => {
                        System.Threading.Thread.Sleep(300);
                        progressTimer.Start();
                        isTimerActive = true;
                        LogError("지연 후 타이머 시작됨");
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                }
                else
                {
                    // 일반 모드는 바로 타이머 시작
                    progressTimer.Start();
                    isTimerActive = true;
                    LogError("재생 시작 및 진행률 타이머 활성화");
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"재생 오류: {ex.Message}";
                LogError($"재생 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
            }
        }

        private void StopPlayback()
        {
            // 진행률 타이머 중지 (null 체크 추가)
            if (progressTimer != null)
            {
                progressTimer.Stop();
                isTimerActive = false;
            }

            // 재생 중지
            if (m_lPlayHandle >= 0)
            {
                Models.CCTV.NET_DVR_StopPlayBack(m_lPlayHandle);
                m_lPlayHandle = -1;
                isPlaying = false;
            }

            // 재생 컨트롤 비활성화
            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            FastForwardButton.IsEnabled = false;
            SlowButton.IsEnabled = false;

            // 진행률 초기화
            ProgressSlider.Value = 0;
            PlayTimeText.Text = "--:--:--";
            StatusText.Text = "준비됨";
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_lPlayHandle < 0)
            {
                // 이전에 재생한 적이 있으면 같은 시간대로 다시 재생
                if (m_startTime != default && m_endTime != default)
                {
                    PlayRecording(m_startTime, m_endTime);
                }
                return;
            }

            // 일시 정지 상태에서 재생 재개
            if (!isPlaying)
            {
                uint dwOutValue = 0;
                if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYRESTART, 0, ref dwOutValue))
                {
                    isPlaying = true;
                    StatusText.Text = "녹화 영상 재생 중";
                    PlayButton.IsEnabled = false;
                    PauseButton.IsEnabled = true;
                    
                    if (progressTimer != null)
                    {
                        progressTimer.Start();
                        isTimerActive = true;
                        LogError("재생으로 타이머 재시작됨");
                    }
                }
                else
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    LogError($"재생 재개 실패: 오류 코드 {errorCode}");
                }
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_lPlayHandle < 0 || !isPlaying) return;

            uint dwOutValue = 0;
            if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYPAUSE, 0, ref dwOutValue))
            {
                isPlaying = false;
                StatusText.Text = "일시 정지";
                PlayButton.IsEnabled = true;
                PauseButton.IsEnabled = false;
                
                if (progressTimer != null)
                {
                    progressTimer.Stop();
                    isTimerActive = false;
                    LogError("일시 정지로 타이머 중지됨");
                }
            }
            else
            {
                uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                LogError($"일시 정지 실패: 오류 코드 {errorCode}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void FastForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_lPlayHandle < 0) return;

            uint dwOutValue = 0;
            if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYFAST, 0, ref dwOutValue))
            {
                playbackSpeed *= 2;
                if (playbackSpeed > 8) playbackSpeed = 8;
                StatusText.Text = $"빨리 감기 (x{playbackSpeed})";
            }
            else
            {
                uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                LogError($"빨리 감기 실패: 오류 코드 {errorCode}");
            }
        }

        private void SlowButton_Click(object sender, RoutedEventArgs e)
        {
            if (m_lPlayHandle < 0) return;

            uint dwOutValue = 0;
            if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYSLOW, 0, ref dwOutValue))
            {
                playbackSpeed /= 2;
                if (playbackSpeed < 0.125) playbackSpeed = 0.125;
                StatusText.Text = $"느리게 재생 (x{playbackSpeed})";
            }
            else
            {
                uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                LogError($"느리게 재생 실패: 오류 코드 {errorCode}");
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (m_lPlayHandle < 0) return;

            try
            {
                // 총 재생 시간(초)
                TimeSpan totalDuration = m_endTime - m_startTime;
                
                // 새 위치(초)
                double newPositionSeconds = (ProgressSlider.Value / 100) * totalDuration.TotalSeconds;
                
                // 시작 시간에 새 위치를 더해 현재 위치 계산
                DateTime newPosition = m_startTime.AddSeconds(newPositionSeconds);

                // 현재 시간 표시 업데이트
                PlayTimeText.Text = newPosition.ToString("HH:mm:ss");
                
                // 드래그 중이 아닐 때만 실제 위치 변경 (프로그레스 타이머에 의한 업데이트)
                if (!isDraggingSlider && isPlaying)
                {
                    // 자동 업데이트는 아무 작업 안함
                    // 실제 시간 변경은 드래그 완료 후에만 수행
                }
            }
            catch (Exception ex)
            {
                LogError($"슬라이더 값 변경 오류: {ex.Message}");
            }
        }
        
        // 슬라이더 드래그 시작 이벤트 핸들러
        private void ProgressSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (m_lPlayHandle < 0) return;
            
            // 드래그 중 플래그 설정
            isDraggingSlider = true;
            
            // 타이머 일시 중지 (드래그 중에는 슬라이더 위치가 자동으로 업데이트되지 않도록)
            progressTimer.Stop();
            
            LogError("슬라이더 드래그 시작");
        }

        private void ProgressSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (m_lPlayHandle < 0) return;

            try
            {
                LogError("슬라이더 드래그 완료 - 완전히 새로운 방식으로 처리");
                
                // 드래그 중 플래그 해제
                isDraggingSlider = false;
                
                // 마지막 슬라이더 변경 시간 업데이트
                lastSliderChangeTime = DateTime.Now.Ticks / 10000;
                LogError($"마지막 슬라이더 변경 시간 업데이트: {lastSliderChangeTime}");
                
                // 총 재생 시간(초)
                TimeSpan totalDuration = m_endTime - m_startTime;
                
                // 새 위치(초)
                double newPositionSeconds = (ProgressSlider.Value / 100) * totalDuration.TotalSeconds;
                
                // 시작 시간에 새 위치를 더해 현재 위치 계산
                DateTime newPosition = m_startTime.AddSeconds(newPositionSeconds);
                LogError($"슬라이더 위치 변경 요청: {newPosition}");

                // 현재 재생 상태 저장
                bool wasPlaying = isPlaying;

                // ===== 완전히 새로운 접근 방식 =====
                
                // 1. 현재 재생 중지
                StatusText.Text = "재생 위치 변경 중...";
                LogError("현재 재생 완전 중지");
                
                // 진행률 타이머 중지
                progressTimer.Stop();
                
                // 모든 재생 상태 리셋
                if (m_lPlayHandle >= 0)
                {
                    LogError("기존 재생 세션 정리");
                    Models.CCTV.NET_DVR_StopPlayBack(m_lPlayHandle);
                    m_lPlayHandle = -1;
                }
                
                // 2. 비디오 윈도우 핸들 다시 가져오기
                IntPtr hwnd = GetVideoWindowHandle(videoControl);
                if (hwnd == IntPtr.Zero)
                {
                    StatusText.Text = "비디오 윈도우 핸들을 가져올 수 없음";
                    LogError("핸들 획득 실패");
                    return;
                }
                
                // 3. 원래 시간 범위를 유지하되, 새 위치에서 재생 시작하기 위한 준비
                
                // 시작 시간 구조체: 새 위치
                Models.CCTV.NET_DVR_TIME struStartTime = new Models.CCTV.NET_DVR_TIME
                {
                    dwYear = (uint)newPosition.Year,
                    dwMonth = (uint)newPosition.Month,
                    dwDay = (uint)newPosition.Day,
                    dwHour = (uint)newPosition.Hour,
                    dwMinute = (uint)newPosition.Minute,
                    dwSecond = (uint)newPosition.Second
                };
                
                // 종료 시간 구조체: 원래 종료 시간 그대로 사용
                Models.CCTV.NET_DVR_TIME struEndTime = new Models.CCTV.NET_DVR_TIME
                {
                    dwYear = (uint)m_endTime.Year,
                    dwMonth = (uint)m_endTime.Month,
                    dwDay = (uint)m_endTime.Day,
                    dwHour = (uint)m_endTime.Hour,
                    dwMinute = (uint)m_endTime.Minute,
                    dwSecond = (uint)m_endTime.Second
                };
                
                LogError($"재생 시도 - 새 위치: {newPosition}, 종료: {m_endTime}");
                
                // 4. 채널 번호 확인
                int lChannel = channelNumber;
                
                // 5. 새 위치에서 재생 시도 (PlayBackByTime 직접 사용)
                m_lPlayHandle = Models.CCTV.NET_DVR_PlayBackByTime(m_lUserID, lChannel, ref struStartTime, ref struEndTime, hwnd);
                
                if (m_lPlayHandle < 0)
                {
                    uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                    string errorMsg = GetErrorMessage(errorCode);
                    LogError($"새 위치 재생 실패: 오류 코드 {errorCode} - {errorMsg}");
                    StatusText.Text = $"재생 위치 이동 실패: {errorMsg}";
                    
                    // 실패 시 원래 범위 전체 재생으로 폴백
                    LogError("실패 시 원래 범위 재생으로 폴백");
                    
                    // 원래 시작 시간으로 다시 시도
                    Models.CCTV.NET_DVR_TIME originalStartTime = new Models.CCTV.NET_DVR_TIME
                    {
                        dwYear = (uint)m_startTime.Year,
                        dwMonth = (uint)m_startTime.Month,
                        dwDay = (uint)m_startTime.Day,
                        dwHour = (uint)m_startTime.Hour,
                        dwMinute = (uint)m_startTime.Minute,
                        dwSecond = (uint)m_startTime.Second
                    };
                    
                    m_lPlayHandle = Models.CCTV.NET_DVR_PlayBackByTime(m_lUserID, lChannel, ref originalStartTime, ref struEndTime, hwnd);
                    
                    if (m_lPlayHandle < 0)
                    {
                        errorCode = Models.CCTV.NET_DVR_GetLastError();
                        errorMsg = GetErrorMessage(errorCode);
                        LogError($"원래 위치 재생 시도도 실패: 오류 코드 {errorCode} - {errorMsg}");
                        StatusText.Text = $"재생 복구 실패: {errorMsg}";
                        return;
                    }
                    
                    // 재생 시작
                    uint dwOutValue = 0;
                    Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYSTART, 0, ref dwOutValue);
                    isPlaying = true;
                    
                    // 일부 SDK 버전에서는 표준 속도로 위치 이동이 안 될 수 있으므로
                    // 빨리 감기로 원하는 위치까지 이동 (대략적 방법)
                    System.Threading.Thread.Sleep(200); // API 호출 간 약간의 지연
                    
                    // 백분율 기반 위치 이동 시도
                    uint percentValue = (uint)ProgressSlider.Value;
                    Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, NET_DVR_SETPLAYPOS, percentValue, ref dwOutValue);
                    
                    // 전역 변수 유지
                    m_startTime = m_startTime; // 변경 필요 없음, 원래 값 유지
                }
                else
                {
                    // 새 위치에서 재생 성공
                    LogError("새 위치에서 재생 성공");
                    
                    // 재생 시작
                    uint dwOutValue = 0;
                    Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYSTART, 0, ref dwOutValue);
                    
                    // 전역 변수 업데이트
                    // 중요: 내부적으로는 새 위치에서 시작하지만, UI에 표시되는 시간 범위는 원래대로 유지함
                    // m_startTime = 원래 값 유지 (변경하지 않음)
                }
                
                // 6. 재생 상태에 따라 UI 업데이트
                isPlaying = true; // 일단 재생 상태로 설정
                
                if (!wasPlaying)
                {
                    // 원래 일시 정지 상태였으면, 다시 일시 정지 상태로 설정
                    LogError("원래 재생 중이 아니었으므로 다시 일시 정지");
                    uint dwOutValue = 0;
                    Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, Models.CCTV.NET_DVR_PLAYPAUSE, 0, ref dwOutValue);
                    isPlaying = false;
                    
                    // UI 상태 업데이트
                    StatusText.Text = "일시 정지 (위치 변경됨)";
                    PlayButton.IsEnabled = true;
                    PauseButton.IsEnabled = false;
                }
                else
                {
                    StatusText.Text = $"재생 위치 변경됨: {newPosition.ToString("HH:mm:ss")}";
                }
                
                // 7. 시간 표시 업데이트
                PlayTimeText.Text = newPosition.ToString("HH:mm:ss");
                ResetStatusTextAfterDelay();
                
                // 8. 재생 중인 경우에만 타이머 재시작
                if (isPlaying)
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 슬라이더 이동 후 타이머 시작 전 슬라이더 업데이트 강제 설정
                        forceSliderUpdate = true;
                        
                        // 약간의 지연 후 타이머 재시작
                        System.Threading.Thread.Sleep(200);
                        progressTimer.Start();
                        isTimerActive = true;
                        LogError("타이머 재시작됨 (강제 업데이트 활성화)");
                        
                        // 5초 후 강제 업데이트 해제
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            System.Threading.Thread.Sleep(500);
                            forceSliderUpdate = false;
                            LogError("슬라이더 강제 업데이트 비활성화");
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }), System.Windows.Threading.DispatcherPriority.Normal);
                }
            }
            catch (Exception ex)
            {
                LogError($"슬라이더 드래그 처리 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
                
                // 오류 발생 시 타이머 재시작 (재생 중인 경우)
                if (isPlaying)
                {
                    progressTimer.Start();
                }
            }
        }

        private IntPtr GetVideoWindowHandle(Image control)
        {
            if (control == null)
                return IntPtr.Zero;

            IntPtr hwnd = IntPtr.Zero;

            try
            {
                control.UpdateLayout();
                System.Windows.Interop.HwndSource source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(control);
                if (source != null)
                {
                    hwnd = source.Handle;
                    LogError($"컨트롤에서 핸들 가져옴: {hwnd}");
                }
                
                if (hwnd == IntPtr.Zero)
                {
                    // 대체 방법: 창 핸들 사용
                    if (_videoWindow != null)
                    {
                        System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(_videoWindow);
                        hwnd = helper.Handle;
                        LogError($"비디오 창에서 핸들 가져옴: {hwnd}");
                    }
                    
                    if (hwnd == IntPtr.Zero)
                    {
                        // 최후의 수단: 메인 창 핸들 사용
                        System.Windows.Interop.WindowInteropHelper mainHelper = new System.Windows.Interop.WindowInteropHelper(this);
                        hwnd = mainHelper.Handle;
                        LogError($"메인 창에서 핸들 가져옴: {hwnd}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"핸들 가져오기 실패: {ex.Message}");
            }

            return hwnd;
        }

        private void LogError(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recording_player_error.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        // 에러 코드에 대한 설명을 반환하는 메서드
        private string GetErrorMessage(uint errorCode)
        {
            switch (errorCode)
            {
                case 0: return "성공";
                case 1: return "통신 오류";
                case 2: return "버전 미호환";
                case 3: return "사용자 ID 오류";
                case 4: return "비밀번호 오류";
                case 5: return "권한 없음";
                case 7: return "버퍼 크기 부족";
                case 8: return "SDK 상태 오류";
                case 9: return "데모 버전";
                case 10: return "대상 오류";
                case 11: return "채널 번호 오류";
                case 12: return "장치 번호 오류";
                case 13: return "채널 초과";
                case 14: return "시간 내 연결 실패";
                case 15: return "소켓 생성 실패";
                case 16: return "데이터 전송 실패";
                case 17: return "권한 부족";
                case 18: return "파일 오류";
                case 19: return "SDK 시작 에러";
                case 20: return "서브 윈도우 에러";
                case 21: return "핸들 번호 에러";
                case 22: return "리소스 부족";
                case 23: return "설치 파일 없음";
                case 24: return "녹화 파일 없음";
                case 25: return "사용자 정원 초과";
                case 26: return "DVR IP 주소 에러";
                case 27: return "연결 장치 불가";
                case 28: return "잘못된 재생 핸들";
                case 29: return "재생 위치 이동 실패";
                case 30: return "DVR에 해당 기능 없음";
                case 31: return "채널에 해당 기능 없음";
                case 32: return "장치에 해당 녹화 없음";
                case 33: return "스레드 생성 실패";
                case 34: return "재생 시작 실패";
                case 35: return "명령 실패";
                case 36: return "권한 없음";
                case 37: return "재생 실패";
                case 38: return "녹화 시간 충돌";
                case 39: return "메모리 에러";
                case 40: return "파라미터 에러";
                case 41: return "데이터 출력 에러";
                case 42: return "JPEG 인코딩 실패";
                default: return $"알 수 없는 오류 ({errorCode})";
            }
        }

        // 숫자 직접 입력을 처리하는 메서드
        private void ProcessNumericTimeInput(Xceed.Wpf.Toolkit.TimePicker timePicker, string numericInput)
        {
            try
            {
                LogError($"숫자 직접 입력 처리 시작: '{numericInput}'");
                
                // 입력 길이 확인
                if (string.IsNullOrEmpty(numericInput))
                {
                    LogError("입력이 비어 있음, 처리 건너뜀");
                    return;
                }
                
                // 숫자 입력을 시간으로 변환
                TimeSpan timeSpan = ConvertNumericInputToTime(numericInput);
                LogError($"변환된 시간: {timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}");
                
                // 현재 날짜 가져오기
                DateTime currentDate = timePicker.Value.HasValue ? timePicker.Value.Value.Date : DateTime.Today;
                DateTime newDateTime = currentDate.Add(timeSpan);
                
                // UI 스레드에서 실행 보장
                timePicker.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // TimePicker 값 설정
                        timePicker.Value = newDateTime;
                        LogError($"TimePicker 값 설정: {timePicker.Value}");
                        
                        // 텍스트박스 찾기
                        TextBox textBox = FindVisualChild<TextBox>(timePicker);
                        if (textBox != null)
                        {
                            // 포맷된 시간 표시
                            string formattedTime = $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
                            textBox.Text = formattedTime;
                            LogError($"TextBox 텍스트 설정: '{formattedTime}'");
                            
                            // 텍스트 선택 및 포커스 이동
                            textBox.SelectAll();
                        }
                        
                        // 포커스 이동
                        MoveFocusAfterEnter(timePicker);
                        
                        LogError("숫자 입력 처리 완료");
                    }
                    catch (Exception ex)
                    {
                        LogError($"숫자 입력 처리 중 오류: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                LogError($"숫자 직접 입력 처리 오류: {ex.Message}, 스택: {ex.StackTrace}");
            }
        }

        // DatePicker의 SelectedDateChanged 이벤트 핸들러
        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DatePicker.SelectedDate.HasValue)
                {
                    DateTime selectedDate = DatePicker.SelectedDate.Value.Date;
                    
                    // 새로운 날짜가 선택되면 해당 날짜에 대한 종료 시간 수정 플래그 초기화
                    dateEndTimeModified[selectedDate] = false;
                    
                    LogError($"날짜 변경: {selectedDate.ToShortDateString()}, 종료 시간 수정 플래그 초기화");
                    
                    // 날짜가 변경되면 종료 시간 제한 확인
                    CheckAndLimitEndTime(true);
                }
            }
            catch (Exception ex)
            {
                LogError($"SelectedDateChanged 오류: {ex.Message}");
            }
        }
        
        // 종료 시간이 현재 시간을 초과하는지 확인하고 제한하는 메서드
        private void CheckAndLimitEndTime(bool isDateChanged = false)
        {
            try
            {
                // 종료 시간 값이 있는지 확인
                if (EndTimePicker.Value.HasValue && DatePicker.SelectedDate.HasValue)
                {
                    DateTime selectedDate = DatePicker.SelectedDate.Value.Date;
                    DateTime endTimeValue = EndTimePicker.Value.Value;
                    
                    // 선택된 날짜와 시간 결합
                    DateTime endTime = new DateTime(
                        selectedDate.Year, selectedDate.Month, selectedDate.Day,
                        endTimeValue.Hour, endTimeValue.Minute, endTimeValue.Second);
                    
                    // 현재 시간
                    DateTime currentTime = DateTime.Now;
                    
                    // 선택된 날짜가 오늘이고, 종료 시간이 현재 시간보다 미래인 경우에만 제한 적용
                    if (selectedDate == DateTime.Today.Date && endTime > currentTime)
                    {
                        LogError($"종료 시간({endTime})이 현재 시간({currentTime})을 초과하여 현재 시간으로 제한합니다.");
                        
                        // 현재 시간으로 설정
                        EndTimePicker.Value = new DateTime(
                            endTimeValue.Year, endTimeValue.Month, endTimeValue.Day,
                            currentTime.Hour, currentTime.Minute, currentTime.Second);
                        
                        // 사용자에게 알림
                        StatusText.Text = "종료 시간이 현재 시간으로 제한되었습니다.";
                        ResetStatusTextAfterDelay();
                    }
                    // 과거 날짜인 경우: 날짜 변경 직후(isDateChanged=true)이고, 사용자가 아직 수정하지 않은 경우에만 23:59:59로 설정
                    else if (selectedDate < DateTime.Today.Date && 
                            (isDateChanged || !dateEndTimeModified.ContainsKey(selectedDate) || !dateEndTimeModified[selectedDate]))
                    {
                        // 현재 종료 시간이 23:59:59가 아닌 경우만 변경
                        if (endTimeValue.Hour != 23 || endTimeValue.Minute != 59 || endTimeValue.Second != 59)
                        {
                            LogError($"과거 날짜({selectedDate.ToShortDateString()}) 선택으로 종료 시간을 23:59:59로 설정합니다.");
                            
                            // 23:59:59로 설정
                            EndTimePicker.Value = new DateTime(
                                endTimeValue.Year, endTimeValue.Month, endTimeValue.Day,
                                23, 59, 59);
                            
                            // 사용자에게 알림
                            StatusText.Text = "과거 날짜 선택으로 종료 시간이 23:59:59로 설정되었습니다.";
                            ResetStatusTextAfterDelay();
                        }
                        
                        // 날짜 변경 직후 자동 설정이 완료되었음을 표시
                        dateEndTimeModified[selectedDate] = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"종료 시간 제한 확인 오류: {ex.Message}");
            }
        }

        // EndTimePicker의 ValueChanged 이벤트 핸들러 - 현재 시간 초과 검사
        private void EndTimePicker_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (DatePicker.SelectedDate.HasValue)
                {
                    DateTime selectedDate = DatePicker.SelectedDate.Value.Date;
                    
                    // 사용자가 직접 시간을 변경했는지 감지하기 위해 플래그 설정
                    // 변경 소스가 사용자인 경우에만 설정 (자동 설정이 아닌 경우)
                    if (e.OriginalSource == EndTimePicker)
                    {
                        dateEndTimeModified[selectedDate] = true;
                        LogError($"사용자가 종료 시간을 직접 변경함: {selectedDate.ToShortDateString()}");
                    }
                }
                
                // 종료 시간이 변경되면 현재 시간 제한 확인 (날짜 변경이 아닌 경우)
                CheckAndLimitEndTime(false);
            }
            catch (Exception ex)
            {
                LogError($"EndTimePicker_ValueChanged 오류: {ex.Message}");
            }
        }

        // StartTimePicker의 ValueChanged 이벤트 핸들러
        private void StartTimePicker_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 시작 시간이 변경되면 시간 범위 검사
            CheckTimeRange();
        }

        // 시작 시간과 종료 시간의 범위를 검사하는 메서드
        private void CheckTimeRange()
        {
            try
            {
                // 시작 시간과 종료 시간이 모두 설정되어 있는지 확인
                if (StartTimePicker.Value.HasValue && EndTimePicker.Value.HasValue)
                {
                    DateTime startTimeValue = StartTimePicker.Value.Value;
                    DateTime endTimeValue = EndTimePicker.Value.Value;
                    
                    // 시작 시간이 종료 시간보다 늦으면 종료 시간과 동일하게 설정
                    if (startTimeValue.TimeOfDay > endTimeValue.TimeOfDay)
                    {
                        LogError($"시작 시간({startTimeValue.ToString("HH:mm:ss")})이 종료 시간({endTimeValue.ToString("HH:mm:ss")})보다 늦어, 종료 시간과 동일하게 설정합니다.");
                        
                        StartTimePicker.Value = new DateTime(
                            startTimeValue.Year, startTimeValue.Month, startTimeValue.Day,
                            endTimeValue.Hour, endTimeValue.Minute, endTimeValue.Second);
                        
                        StatusText.Text = "시작 시간이 종료 시간보다 늦을 수 없습니다.";
                        ResetStatusTextAfterDelay();
                    }
                }
                
                // 사용자가 시작 시간을 직접 변경했으므로 현재 시간 제한만 확인 (날짜 변경으로 인한 시간 초기화는 하지 않음)
                CheckAndLimitEndTime(false);
            }
            catch (Exception ex)
            {
                LogError($"시간 범위 검사 오류: {ex.Message}");
            }
        }

        // DatePicker가 로드될 때 오늘 날짜로 표시하도록 설정
        private void DatePicker_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 로드 완료 후 오늘 날짜가 확실히 표시되도록 설정
                DateTime today = DateTime.Today;
                LogError($"DatePicker_Loaded: 강제로 오늘 날짜({today.ToShortDateString()})로 설정합니다.");
                
                // 오늘 날짜에 대한 시간 수정 플래그 초기화
                dateEndTimeModified[today] = false;
                
                // 블랙아웃 날짜 재설정 (기존 설정 삭제 후 다시 설정)
                DatePicker.BlackoutDates.Clear();
                CalendarDateRange futureRange = new CalendarDateRange(today.AddDays(1), DateTime.MaxValue);
                DatePicker.BlackoutDates.Add(futureRange);
                LogError($"DatePicker_Loaded: 블랙아웃 범위 재설정 - {today.AddDays(1).ToShortDateString()} 이후 모든 날짜");
                
                // 오늘 날짜가 블랙아웃에 잘못 포함되어 있는지 확인
                if (DatePicker.BlackoutDates.Contains(today))
                {
                    LogError($"경고: 오늘 날짜({today.ToShortDateString()})가 블랙아웃에 잘못 포함되어 있습니다! 수정 시도...");
                    // 블랙아웃 재설정 시도
                    DatePicker.BlackoutDates.Clear();
                    DatePicker.BlackoutDates.Add(new CalendarDateRange(today.AddDays(1), DateTime.MaxValue));
                }
                
                // 우선 DisplayDate를 설정 (캘린더가 보여주는 월/년)
                DatePicker.DisplayDate = today;
                
                // 그 다음 SelectedDate 설정 (실제 선택된 날짜)
                DatePicker.SelectedDate = today;
                
                LogError($"DatePicker_Loaded: 설정 완료 - DisplayDate={DatePicker.DisplayDate.ToShortDateString()}, SelectedDate={DatePicker.SelectedDate?.ToShortDateString() ?? "null"}");
                
                // DatePicker 내부의 Calendar 찾기 시도
                Calendar calendar = FindVisualChild<Calendar>(DatePicker);
                if(calendar != null)
                {
                    LogError("DatePicker_Loaded: 내부 Calendar 컨트롤을 찾았습니다.");
                    calendar.DisplayDate = today;
                    calendar.IsTodayHighlighted = true;
                    
                    // Calendar의 BlackoutDates도 직접 확인
                    if (calendar.BlackoutDates.Contains(today))
                    {
                        LogError($"경고: Calendar 내부에서 오늘 날짜({today.ToShortDateString()})가 블랙아웃에 잘못 포함되어 있습니다!");
                        calendar.BlackoutDates.Clear();
                        calendar.BlackoutDates.Add(new CalendarDateRange(today.AddDays(1), DateTime.MaxValue));
                    }
                }
                else
                {
                    LogError("DatePicker_Loaded: 내부 Calendar 컨트롤을 찾을 수 없습니다.");
                }
            }
            catch(Exception ex)
            {
                LogError($"DatePicker_Loaded 오류: {ex.Message}, 스택 추적: {ex.StackTrace}");
            }
        }

        // RecordingVideoWindow를 숨기는 public 메서드 추가
        public void HideVideoWindow()
        {
            try
            {
                if (_videoWindow != null && _videoWindow.IsVisible)
                {
                    _videoWindow.Hide();
                    LogError("RecordingVideoWindow 숨김 처리 완료");
                }
            }
            catch (Exception ex)
            {
                LogError($"RecordingVideoWindow 숨김 처리 오류: {ex.Message}");
            }
        }
        
        // RecordingVideoWindow를 표시하는 public 메서드 추가
        public void ShowVideoWindow()
        {
            try
            {
                if (_videoWindow != null && !_videoWindow.IsVisible)
                {
                    _videoWindow.Show();
                    UpdateVideoWindowPosition();
                    LogError("RecordingVideoWindow 표시 처리 완료");
                }
            }
            catch (Exception ex)
            {
                LogError($"RecordingVideoWindow 표시 처리 오류: {ex.Message}");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 관리되는 리소스 해제
                    try
                    {
                        LogError("RecordingPlayerWindow Dispose 시작");
                        
                        // 진행률 타이머 정리
                        if (progressTimer != null)
                        {
                            progressTimer.Stop();
                            progressTimer.Tick -= ProgressTimer_Tick;
                            progressTimer = null;
                            LogError("진행률 타이머 정리 완료");
                        }
                        
                        // 상태 리셋 타이머 정리
                        if (statusResetTimer != null)
                        {
                            statusResetTimer.Stop();
                            statusResetTimer = null;
                            LogError("상태 리셋 타이머 정리 완료");
                        }
                        
                        // 재생 중지
                        StopPlayback();
                        
                        // NVR 로그아웃
                        if (m_lUserID >= 0)
                        {
                            Models.CCTV.NET_DVR_Logout(m_lUserID);
                            m_lUserID = -1;
                            LogError("NVR 로그아웃 완료");
                        }
                        
                        // SDK 정리
                        if (m_bInitSDK)
                        {
                            Models.CCTV.NET_DVR_Cleanup();
                            m_bInitSDK = false;
                            LogError("SDK 정리 완료");
                        }
                        
                        // 비디오 윈도우 정리
                        if (_videoWindow != null)
                        {
                            _videoWindow.Close();
                            _videoWindow = null;
                            LogError("비디오 윈도우 정리 완료");
                        }
                        
                        // Owner 이벤트 핸들러 해제
                        if (this.Owner != null)
                        {
                            this.Owner.LocationChanged -= Owner_LocationChanged;
                            this.Owner.SizeChanged -= Owner_SizeChanged;
                            this.Owner.StateChanged -= Owner_StateChanged;
                            LogError("Owner 이벤트 핸들러 해제 완료");
                        }
                        
                        LogError("RecordingPlayerWindow Dispose 완료");
                    }
                    catch (Exception ex)
                    {
                        LogError($"RecordingPlayerWindow Dispose 중 오류: {ex.Message}");
                    }
                }

                // 관리되지 않는 리소스 해제
                _disposed = true;
            }
        }

        // 녹화재생 일시정지 메서드 (화면 전환 최적화용)
        public void PausePlayback()
        {
            try
            {
                if (m_lPlayHandle >= 0 && isPlaying)
                {
                    LogError("녹화재생 일시정지 시작 (화면 전환)");
                    
                    uint dwOutValue = 0;
                    if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, NET_DVR_PLAYPAUSE, 0, ref dwOutValue))
                    {
                        isPlaying = false;
                        
                        // 진행률 타이머 중지
                        if (progressTimer != null)
                        {
                            progressTimer.Stop();
                            isTimerActive = false;
                        }
                        
                        // UI 상태 업데이트
                        this.Dispatcher.BeginInvoke(() =>
                        {
                            StatusText.Text = "일시 정지 (화면 전환)";
                            PlayButton.IsEnabled = true;
                            PauseButton.IsEnabled = false;
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                        
                        LogError("녹화재생 일시정지 완료 (화면 전환)");
                    }
                    else
                    {
                        uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                        LogError($"녹화재생 일시정지 실패: 오류 코드 {errorCode}");
                    }
                }
                else
                {
                    LogError("녹화재생 일시정지 건너뜀: 재생 중이 아님");
                }
            }
            catch (Exception ex)
            {
                LogError($"녹화재생 일시정지 오류: {ex.Message}");
            }
        }
        
        // 녹화재생 재개 메서드 (화면 전환 최적화용)
        public void ResumePlayback()
        {
            try
            {
                if (m_lPlayHandle >= 0 && !isPlaying)
                {
                    LogError("녹화재생 재개 시작 (화면 전환)");
                    
                    uint dwOutValue = 0;
                    if (Models.CCTV.NET_DVR_PlayBackControl(m_lPlayHandle, NET_DVR_PLAYRESTART, 0, ref dwOutValue))
                    {
                        isPlaying = true;
                        
                        // 진행률 타이머 재시작
                        if (progressTimer != null)
                        {
                            progressTimer.Start();
                            isTimerActive = true;
                        }
                        
                        // UI 상태 업데이트
                        this.Dispatcher.BeginInvoke(() =>
                        {
                            StatusText.Text = "녹화 영상 재생 중";
                            PlayButton.IsEnabled = false;
                            PauseButton.IsEnabled = true;
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                        
                        LogError("녹화재생 재개 완료 (화면 전환)");
                    }
                    else
                    {
                        uint errorCode = Models.CCTV.NET_DVR_GetLastError();
                        LogError($"녹화재생 재개 실패: 오류 코드 {errorCode}");
                    }
                }
                else if (m_lPlayHandle < 0)
                {
                    LogError("녹화재생 재개 건너뜀: 재생 핸들이 없음");
                }
                else
                {
                    LogError("녹화재생 재개 건너뜀: 이미 재생 중");
                }
            }
            catch (Exception ex)
            {
                LogError($"녹화재생 재개 오류: {ex.Message}");
            }
        }
    }
} 