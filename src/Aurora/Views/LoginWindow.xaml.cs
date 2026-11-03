using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using TdLib;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>Same flow as the official apps: phone number, code, cloud password if set.</summary>
public partial class LoginWindow : Window
{
    enum Step { Phone, Code, Pass, Api, Wait }
    Step _step = Step.Wait;
    int _codeLength;
    bool _submitting;
    int _resendIn;
    readonly DispatcherTimer _timer;

    public LoginWindow()
    {
        InitializeComponent();
        App.Chrome(this);
        Icon = Icons.Bitmap("online");
        Logo.Source = Icons.Get("online");
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TickResend(), Dispatcher);
        Loaded += (_, _) => { if (_step == Step.Wait) Show(App.Cfg.HasApi ? Step.Wait : Step.Api); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Tg.ConnectionChanged += OnConnection;
        Closed += (_, _) => Tg.ConnectionChanged -= OnConnection;
    }

    /// <summary>While TDLib cannot reach Telegram, say so in the footer: the usual fix is a proxy.</summary>
    void OnConnection(string text)
    {
        if (_submitting) return;
        Busy.Text = text.Length > 0 ? text : "";
    }

    public void OnAuthState(TdApi.AuthorizationState s)
    {
        _submitting = false;
        switch (s)
        {
            case TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber:
                Show(Step.Phone);
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitCode c:
                ShowCode(c.CodeInfo);
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitPassword p:
                Show(Step.Pass);
                if (!string.IsNullOrEmpty(p.PasswordHint)) Hint.Text += "\n" + L("Подсказка: {0}", p.PasswordHint);
                break;
            case TdApi.AuthorizationState.AuthorizationStateWaitRegistration:
                Show(Step.Phone);
                Error.Text = L("Этот номер ещё не зарегистрирован в Telegram. Создайте аккаунт в официальном приложении, потом войдите здесь.");
                break;
            default:
                Show(Step.Wait);
                Error.Text = L("Этот способ входа пока не поддерживается: ") + s.GetType().Name.Replace("AuthorizationState", "");
                break;
        }
    }

    public void ShowApiError(string err)
    {
        if (Config.HasBuiltInKeys)
        {
            Show(Step.Wait);
            Error.Text = L("Telegram отклонил ключи этой сборки: ") + err;
            return;
        }
        Show(Step.Api);
        ApiId.Text = App.Cfg.ApiId > 0 ? App.Cfg.ApiId.ToString() : "";
        ApiHash.Text = App.Cfg.ApiHash;
        Error.Text = L("Telegram не принял ключи: ") + err;
    }

    void ShowCode(TdApi.AuthenticationCodeInfo info)
    {
        Show(Step.Code);
        var phone = "+" + info.PhoneNumber.TrimStart('+');
        (_codeLength, Hint.Text) = info.Type switch
        {
            TdApi.AuthenticationCodeType.AuthenticationCodeTypeTelegramMessage t =>
                (t.Length, L("Мы отправили код в приложение Telegram на другом вашем устройстве ({0}).", phone)),
            TdApi.AuthenticationCodeType.AuthenticationCodeTypeSms s => (s.Length, L("Мы отправили SMS с кодом на {0}.", phone)),
            TdApi.AuthenticationCodeType.AuthenticationCodeTypeCall c => (c.Length, L("Вам позвонят на {0} и продиктуют код.", phone)),
            _ => (0, L("Код отправлен на {0}.", phone)),
        };
        Code.Clear();

        ResendText.Text = info.NextType switch
        {
            TdApi.AuthenticationCodeType.AuthenticationCodeTypeSms => L("Отправить код по SMS"),
            TdApi.AuthenticationCodeType.AuthenticationCodeTypeCall => L("Позвонить мне"),
            null => "",
            _ => L("Отправить код другим способом"),
        };
        _resendIn = info.NextType == null ? 0 : info.Timeout;
        TickResend();
        _timer.Start();
    }

    void TickResend()
    {
        var hasNext = ResendText.Text.Length > 0;
        if (_resendIn > 0)
        {
            ResendLink.IsEnabled = false;
            ResendTimer.Text = L("Повторно через {0}:{1:00}", _resendIn / 60, _resendIn % 60);
            _resendIn--;
        }
        else
        {
            _timer.Stop();
            ResendLink.IsEnabled = hasNext;
            ResendTimer.Text = "";
        }
        ((FrameworkElement)ResendLink.Parent).Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
    }

    void Show(Step step)
    {
        _step = step;
        PhoneStep.Visibility = step == Step.Phone ? Visibility.Visible : Visibility.Collapsed;
        CodeStep.Visibility = step == Step.Code ? Visibility.Visible : Visibility.Collapsed;
        PassStep.Visibility = step == Step.Pass ? Visibility.Visible : Visibility.Collapsed;
        ApiStep.Visibility = step == Step.Api ? Visibility.Visible : Visibility.Collapsed;
        BackBtn.Visibility = step is Step.Code or Step.Pass ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.IsEnabled = step != Step.Wait;
        Busy.Text = step == Step.Wait ? L("Подключаюсь…") : "";
        Error.Text = "";
        if (step != Step.Code) _timer.Stop();
        (Heading.Text, Hint.Text) = step switch
        {
            Step.Phone => (L("Вход в Telegram"), L("Подтвердите код страны и введите свой номер телефона.")),
            Step.Code => (L("Код подтверждения"), ""),
            Step.Pass => (L("Двухэтапная проверка"), L("На аккаунте включён облачный пароль. Введите его.")),
            Step.Api => (L("Ключи приложения"),
                L("Эта сборка Aurora сделана без встроенных ключей Telegram API. Получите свои на my.telegram.org, это бесплатно.")),
            _ => (L("Подключение"), L("Секунду…")),
        };
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (step)
            {
                case Step.Phone: Phone.Focus(); Phone.CaretIndex = Phone.Text.Length; break;
                case Step.Code: Code.Focus(); break;
                case Step.Pass: Pass.Focus(); break;
                case Step.Api: ApiId.Focus(); break;
            }
        });
    }

    // ---------- input helpers ----------

    void Phone_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(ch => char.IsDigit(ch) || ch is '+' or ' ' or '-' or '(' or ')');

    /// <summary>Like the official apps: the code goes out as soon as the last digit is typed.</summary>
    void Code_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var digits = new string(Code.Text.Where(char.IsDigit).ToArray());
        if (_codeLength > 0 && digits.Length == _codeLength && !_submitting) Next_Click(this, new RoutedEventArgs());
    }

    // ---------- actions ----------

    void Back_Click(object sender, RoutedEventArgs e)
    {
        // TDLib accepts a new phone number while waiting for the code or password.
        Show(Step.Phone);
    }

    async void Resend_Click(object sender, RoutedEventArgs e)
    {
        try { await Tg.Client.ExecuteAsync(new TdApi.ResendAuthenticationCode()); }
        catch (TdException ex) { Error.Text = Humanize(ex.Error?.Message ?? ex.Message); }
    }

    async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting) return;
        Error.Text = "";
        _submitting = true;
        NextBtn.IsEnabled = false;
        Busy.Text = L("Проверяю…");
        try
        {
            switch (_step)
            {
                case Step.Phone:
                    var phone = new string(Phone.Text.Where(char.IsDigit).ToArray());
                    if (phone.Length < 7) throw new InvalidOperationException(L("Введите номер полностью, вместе с кодом страны."));
                    await Tg.Client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber { PhoneNumber = "+" + phone });
                    break;
                case Step.Code:
                    await Tg.Client.ExecuteAsync(new TdApi.CheckAuthenticationCode { Code = new string(Code.Text.Where(char.IsDigit).ToArray()) });
                    break;
                case Step.Pass:
                    await Tg.Client.ExecuteAsync(new TdApi.CheckAuthenticationPassword { Password = Pass.Password });
                    break;
                case Step.Api:
                    if (!int.TryParse(ApiId.Text.Trim(), out var id) || ApiHash.Text.Trim().Length < 16)
                        throw new InvalidOperationException(L("api_id — число, api_hash — строка из 32 символов."));
                    App.Cfg.ApiId = id;
                    App.Cfg.ApiHash = ApiHash.Text.Trim();
                    App.Cfg.Save();
                    Show(Step.Wait);
                    _submitting = false;
                    App.Current.StartClient();
                    return;
            }
            Busy.Text = L("Подождите…");
            // The next auth state arrives as an update and moves the wizard on; if TDLib re-sends the
            // same state (e.g. after "Назад"), let the user try again.
            _ = Task.Delay(4000).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                if (!_submitting) return;
                _submitting = false;
                NextBtn.IsEnabled = true;
                Busy.Text = "";
            }));
        }
        catch (TdException ex) { Fail(Humanize(ex.Error?.Message ?? ex.Message)); }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void Fail(string message)
    {
        Error.Text = message;
        _submitting = false;
        NextBtn.IsEnabled = true;
        Busy.Text = "";
        if (_step == Step.Code) { Code.Clear(); Code.Focus(); }
        if (_step == Step.Pass) { Pass.Clear(); Pass.Focus(); }
    }

    static string Humanize(string err) => err switch
    {
        "PHONE_NUMBER_INVALID" => L("Неверный номер телефона."),
        "PHONE_NUMBER_BANNED" => L("Этот номер заблокирован в Telegram."),
        "PHONE_CODE_INVALID" => L("Неверный код."),
        "PHONE_CODE_EXPIRED" => L("Код устарел, запросите новый."),
        "PASSWORD_HASH_INVALID" => L("Неверный пароль."),
        "API_ID_INVALID" => L("Неверные api_id/api_hash."),
        _ when err.StartsWith("FLOOD_WAIT") || err.StartsWith("Too Many Requests") => L("Слишком много попыток, подождите немного."),
        _ => err,
    };

    void Link_Click(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
