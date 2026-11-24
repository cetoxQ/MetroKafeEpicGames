using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using System.Windows.Media;

namespace MetroKafeEpicGames.Views;

public partial class JsonEditor : UserControl, IDialogBody
{
    private readonly string _id;

    public JsonObject? Result { get; private set; }

    public JsonEditor(string json, string id)
    {
        InitializeComponent();
        _id = id;
        JsonBox.Text = json;
    }

    private void JsonBox_TextChanged(object sender, TextChangedEventArgs e) => Check();

    private bool Check()
    {
        try
        {
            if (JsonNode.Parse(JsonBox.Text) is not JsonObject obj) return Fail("Bir JSON nesnesi ({ … }) olmalı.");
            var id = obj["installationId"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (id != _id) return Fail("installationId değiştirilemez: " + _id);
            Result = obj;
            StatusText.Text = "✓ Geçerli JSON";
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
            return true;
        }
        catch (JsonException ex)
        {
            return Fail($"JSON hatası (satır {ex.LineNumber + 1}): {ex.Message.Split('.')[0]}");
        }
    }

    private bool Fail(string message)
    {
        Result = null;
        StatusText.Text = "⚠ " + message;
        StatusText.Foreground = (Brush)FindResource("DangerBrush");
        return false;
    }

    public bool Validate() => Check();
}
