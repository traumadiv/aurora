using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aurora.Models;
using Aurora.Services;

namespace Aurora.Views;

/// <summary>"User details": who is this, as far as Telegram tells us.</summary>
public partial class InfoWindow : Window
{
    readonly ChatItem _chat;

    public InfoWindow(ChatItem chat)
    {
        InitializeComponent();
        App.Chrome(this);
        _chat = chat;
        DataContext = chat;
        Title = chat.Kind switch
        {
            ChatKind.Group => L("О группе"),
            ChatKind.Channel => L("О канале"),
            _ => L("Информация о пользователе"),
        };
        Icon = Icons.Bitmap(chat.Icon);
        Loaded += async (_, _) => await Fill();
    }

    async Task Fill()
    {
        if (Tg.Users.TryGetValue(_chat.UserId, out var u))
        {
            Row(L("Имя:"), (u.FirstName + " " + u.LastName).Trim());
            var nick = u.Usernames?.ActiveUsernames?.FirstOrDefault();
            if (nick != null) Row(L("Ник:"), "@" + nick);
            if (!string.IsNullOrEmpty(u.PhoneNumber)) Row(L("Телефон:"), "+" + u.PhoneNumber);
            Row(L("Статус:"), _chat.StatusText);
            Row(L("В контактах:"), u.IsContact ? (u.IsMutualContact ? L("да, взаимно") : L("да")) : L("нет"));
        }
        else
        {
            Row(L("Тип:"), _chat.Kind == ChatKind.Channel ? L("канал") : L("группа"));
            Row(L("Участники:"), _chat.StatusText);
        }
        var about = await Tg.About(_chat);
        if (about.Length > 0) Row(_chat.Kind is ChatKind.User or ChatKind.Saved ? L("О себе:") : L("Описание:"), about, smileys: true);
        Row("ID:", _chat.Id.ToString(), gray: true);
    }

    void Row(string label, string value, bool smileys = false, bool gray = false)
    {
        var r = Fields.RowDefinitions.Count;
        Fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new TextBlock { Text = label, Margin = new Thickness(0, 3, 10, 3) };
        l.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        // Selectable value: people copy phone numbers and nicks from here.
        FrameworkElement v;
        if (smileys)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Smileys.SetText(tb, value);
            v = tb;
        }
        else
        {
            v = new TextBox
            {
                Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0), Margin = new Thickness(-2, 3, 0, 3),
            };
            v.SetResourceReference(Control.ForegroundProperty, gray ? "Muted" : "Ink");
        }
        Grid.SetRow(l, r);
        Grid.SetRow(v, r);
        Grid.SetColumn(v, 1);
        Fields.Children.Add(l);
        Fields.Children.Add(v);
    }

    void Write_Click(object sender, RoutedEventArgs e)
    {
        App.Current.OpenChat(_chat.Id);
        Close();
    }
}
