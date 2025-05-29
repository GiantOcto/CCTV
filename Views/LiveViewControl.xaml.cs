using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CCTV.Views
{
    public partial class LiveViewControl : UserControl
    {
        // 이벤트 정의 - 메인 윈도우에서 구독할 수 있도록
        public event EventHandler<string> StatusChanged;

        public LiveViewControl()
        {
            InitializeComponent();
            OnStatusChanged("실시간 CCTV 시스템이 준비되었습니다. 채널을 선택하세요.");
        }

        // 상태 변경 이벤트 발생
        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        // CCTV 연결 버튼 클릭
        private void CCTVButton_Click(object sender, RoutedEventArgs e)
        {
            OnStatusChanged("채널 버튼을 클릭하여 CCTV에 연결하세요.");
        }

        // 채널 버튼 클릭 이벤트
        private void ChannelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is ToggleButton button && button.Tag is string channelStr)
                {
                    if (int.TryParse(channelStr, out int channel))
                    {
                        // 메인 윈도우의 채널 버튼 클릭 시뮬레이션
                        var mainWindow = GetMainWindow();
                        if (mainWindow != null)
                        {
                            // 메인 윈도우의 채널 버튼 찾기
                            var mainChannelButton = mainWindow.FindName($"Channel{channel}Button") as ToggleButton;
                            if (mainChannelButton != null)
                            {
                                // 메인 윈도우의 채널 버튼 클릭 시뮬레이션
                                mainChannelButton.IsChecked = true;
                                
                                // 메인 윈도우의 채널 버튼 클릭 이벤트 실행
                                var clickEvent = new RoutedEventArgs(ToggleButton.ClickEvent);
                                mainChannelButton.RaiseEvent(clickEvent);
                                
                                OnStatusChanged($"채널 {channel}번이 선택되었습니다.");
                                
                                // 다른 채널 버튼들 해제
                                UncheckAllChannelButtons();
                                button.IsChecked = true;
                                
                                // 현재 채널 텍스트 업데이트
                                CurrentChannelText.Text = $"현재 채널: {channel}번";
                            }
                            else
                            {
                                OnStatusChanged($"메인 윈도우에서 채널 {channel}번 버튼을 찾을 수 없습니다.");
                            }
                        }
                        else
                        {
                            OnStatusChanged("메인 윈도우를 찾을 수 없습니다.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged($"채널 변경 오류: {ex.Message}");
            }
        }

        private MainWindow? GetMainWindow()
        {
            var window = Window.GetWindow(this);
            return window as MainWindow;
        }

        private void UncheckAllChannelButtons()
        {
            Channel33Button.IsChecked = false;
            Channel34Button.IsChecked = false;
            Channel35Button.IsChecked = false;
            Channel36Button.IsChecked = false;
        }

        // PTZ 제어 메서드들
        private void Preset1_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("프리셋 1");
        }

        private void Preset2_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("프리셋 2");
        }

        private void TiltUp_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("위로 기울이기");
        }

        private void TiltDown_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("아래로 기울이기");
        }

        private void PanLeft_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("왼쪽으로 회전");
        }

        private void PanRight_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("오른쪽으로 회전");
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("확대");
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ExecutePTZCommand("축소");
        }

        private void ExecutePTZCommand(string commandName)
        {
            try
            {
                var mainWindow = GetMainWindow();
                if (mainWindow != null)
                {
                    // 메인 윈도우의 같은 이름의 메서드 호출 시뮬레이션
                    OnStatusChanged($"{commandName} 명령이 메인 윈도우로 전달되었습니다.");
                }
                else
                {
                    OnStatusChanged("메인 윈도우를 찾을 수 없습니다.");
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged($"{commandName} 오류: {ex.Message}");
            }
        }
    }
} 