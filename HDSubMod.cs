using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Modding;
using OpenIGI.Modding.Presentation;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;
using OpenIGI.UI.Text;

namespace HDSubMod;

public enum SubtitleLanguage
{
    English,
    Sinhala,
    Tamil,
    Hindi,
    Russian,
    Spanish,
    German
}

public static class ModSettingsExtensions
{
    public static EnumCVar<TEnum> Choice<TEnum>(
        this ModSettings settings,
        string name,
        TEnum defaultValue,
        string description) where TEnum : struct, Enum
    {
        var regField = typeof(ModSettings).GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var registry = (CVarRegistry)regField.GetValue(settings)!;
        string fullName = settings.NameOf(name);
        if (registry.TryGet(fullName, out var existing) && existing is EnumCVar<TEnum> cvar)
        {
            return cvar;
        }

        return CVar.Choice(fullName, defaultValue, CVarOptions.Archive, description, registry);
    }
}

public sealed class Mod : IOpenIgiMod
{
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        BoolCVar enabled = builder.Settings.Bool("enabled", true, "Enable modern HD cutscene subtitles.");
        EnumCVar<SubtitleLanguage> language = builder.Settings.Choice(
            "language",
            SubtitleLanguage.English,
            "Subtitle language for cutscenes and briefings.");

        builder.Presenters(presenters =>
        {
            presenters.Replace<HudPresentationRequest, IHudPresenter>((in HudPresentationRequest request) =>
            {
                var asm = Assembly.Load("OpenIGI.Presentation.Igi1");
                var ssType = asm.GetType("OpenIGI.Presentation.Igi1.World.StatusScreenRenderer")!;
                var original = (IHudPresenter)Activator.CreateInstance(ssType, request.Device, request.Logger)!;
                var hdStatusRenderer = new StatusMessageRenderer(request.Device, request.Logger);

                return new CustomHudPresenter(original, hdStatusRenderer, request.Device, enabled, language, builder.Directory);
            });
        });
    }
}

/// <summary>
/// Wraps the HUD presenter:
/// - Implements IHudIconSource to preserve full in-game HUD rendering (weapons, ammo, health, radar).
/// - Forwards in-game messages directly to the native HUD so gameplay is 100% untouched.
/// - Upgrades cutscene subtitles to modern HD TrueType typography, positioned cleanly in the bottom black letterbox.
/// - Full multi-language support (English, Sinhala, Tamil, Hindi, Russian, Spanish, German).
/// - Dynamically bakes and loads dedicated Google Noto and Windows TrueType fonts with full native script glyphs.
/// - Character speaker highlights (Anya, Jones, Priboi, Ekk, Harrison) with dynamic color themes.
/// </summary>
public sealed class CustomHudPresenter : IHudPresenter, IHudIconSource, IDisposable
{
    private readonly IHudPresenter _inner;
    private readonly StatusMessageRenderer _statusRenderer;
    private readonly IGraphicsDevice _device;
    private readonly BoolCVar _enabled;
    private readonly EnumCVar<SubtitleLanguage> _language;
    private readonly string _modDirectory;

    private UiFont? _defaultFont;
    private readonly FieldInfo? _bodyField;
    private readonly Dictionary<SubtitleLanguage, UiFont> _languageFonts = new();
    private readonly Dictionary<SubtitleLanguage, Dictionary<string, string>> _translations = new();

    public CustomHudPresenter(
        IHudPresenter inner,
        StatusMessageRenderer statusRenderer,
        IGraphicsDevice device,
        BoolCVar enabled,
        EnumCVar<SubtitleLanguage> language,
        string modDirectory)
    {
        _inner = inner;
        _statusRenderer = statusRenderer;
        _device = device;
        _enabled = enabled;
        _language = language;
        _modDirectory = modDirectory;
        _bodyField = typeof(StatusMessageRenderer).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);

        InitializeBuiltInTranslations();
        LoadExternalTranslations();
    }

    public bool IsReady => _inner.IsReady && _statusRenderer.IsReady;

    public bool Load(VirtualFileSystem fileSystem)
    {
        bool ok = _inner.Load(fileSystem);
        _statusRenderer.Load();
        EnsureDefaultFont();
        return ok;
    }

    private void EnsureDefaultFont()
    {
        if (_defaultFont != null) return;

        if (_bodyField != null)
        {
            _defaultFont = (UiFont?)_bodyField.GetValue(_statusRenderer);
        }

        if (_defaultFont == null && _device != null && UiFontLibrary.TypographyAvailable)
        {
            try
            {
                _defaultFont = UiFontLibrary.Typography(_device).Body;
            }
            catch
            {
                // Ignored - fallback will be used if unavailable
            }
        }
    }

    private UiFont GetFontForLanguage(SubtitleLanguage language)
    {
        EnsureDefaultFont();

        if (language == SubtitleLanguage.English)
        {
            return _defaultFont!;
        }

        if (_languageFonts.TryGetValue(language, out var cached))
        {
            return cached;
        }

        try
        {
            byte[]? fontBytes = null;
            string fontFileName = language switch
            {
                SubtitleLanguage.Sinhala => "NotoSansSinhala-Regular.ttf",
                SubtitleLanguage.Tamil => "NotoSansTamil-Regular.ttf",
                SubtitleLanguage.Hindi => "NotoSansDevanagari-Regular.ttf",
                _ => ""
            };

            if (!string.IsNullOrEmpty(fontFileName))
            {
                string[] searchDirs = {
                    Path.Combine(_modDirectory, "fonts"),
                    Path.Combine(AppContext.BaseDirectory, "fonts"),
                    Path.Combine(AppContext.BaseDirectory, "mod", "fonts"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Modding", "HDSubMod", "fonts")
                };

                foreach (var dir in searchDirs)
                {
                    string candidate = Path.Combine(dir, fontFileName);
                    if (File.Exists(candidate))
                    {
                        fontBytes = File.ReadAllBytes(candidate);
                        break;
                    }
                }
            }
            else if (language == SubtitleLanguage.Russian)
            {
                string[] winFonts = {
                    @"C:\Windows\Fonts\segoeui.ttf",
                    @"C:\Windows\Fonts\arial.ttf",
                    @"C:\Windows\Fonts\tahoma.ttf"
                };

                foreach (var wf in winFonts)
                {
                    if (File.Exists(wf))
                    {
                        fontBytes = File.ReadAllBytes(wf);
                        break;
                    }
                }
            }

            if (fontBytes != null && _device != null)
            {
                var codepoints = GetCodepointsForLanguage(language);
                var atlas = FontAtlasBuilder.Build(
                    fontBytes,
                    28f,
                    FontAtlasKind.Coverage,
                    codepoints,
                    1024,
                    4,
                    128);

                var loaded = RhiFontExtensions.Upload(_device, atlas);
                _languageFonts[language] = loaded;
                return loaded;
            }
        }
        catch
        {
            // Silently fallback if font atlas build fails
        }

        return _defaultFont!;
    }

    private static List<int> GetCodepointsForLanguage(SubtitleLanguage lang)
    {
        var set = new HashSet<int>();
        // Standard printable ASCII (32..126)
        for (int i = 32; i <= 126; i++) set.Add(i);

        switch (lang)
        {
            case SubtitleLanguage.Russian:
                for (int i = 0x0400; i <= 0x04FF; i++) set.Add(i); // Cyrillic
                set.Add(0x0401); // Ё
                set.Add(0x0451); // ё
                break;

            case SubtitleLanguage.Sinhala:
                for (int i = 0x0D80; i <= 0x0DFF; i++) set.Add(i); // Sinhala Unicode block
                set.Add(0x200B); // Zero Width Space
                set.Add(0x200C); // Zero Width Non-Joiner
                set.Add(0x200D); // Zero Width Joiner
                break;

            case SubtitleLanguage.Tamil:
                for (int i = 0x0B80; i <= 0x0BFF; i++) set.Add(i); // Tamil Unicode block
                set.Add(0x200B);
                set.Add(0x200C);
                set.Add(0x200D);
                break;

            case SubtitleLanguage.Hindi:
                for (int i = 0x0900; i <= 0x097F; i++) set.Add(i); // Devanagari Unicode block
                set.Add(0x200B);
                set.Add(0x200C);
                set.Add(0x200D);
                break;

            case SubtitleLanguage.Spanish:
            case SubtitleLanguage.German:
                for (int i = 160; i <= 383; i++) set.Add(i); // Latin-1 Supplement & Extended-A
                break;
        }

        return set.OrderBy(x => x).ToList();
    }

    public void Tick() => _inner.Tick();

    public void Draw(
        SpriteBatch batch,
        WeaponType? weapon,
        int roundsInClip,
        int reserve,
        int secondSubCounter,
        int m203Reserve,
        int screenWidth,
        int screenHeight,
        int scale)
    {
        _inner.Draw(batch, weapon, roundsInClip, reserve, secondSubCounter, m203Reserve, screenWidth, screenHeight, scale);
    }

    public void DrawPlayerStatus(
        SpriteBatch batch,
        float healthFraction,
        float? interactionProgress,
        int? missionTimeRemainingTicks,
        int screenWidth,
        int screenHeight,
        int scale)
    {
        _inner.DrawPlayerStatus(batch, healthFraction, interactionProgress, missionTimeRemainingTicks, screenWidth, screenHeight, scale);
    }

    public void DrawSniperScope(SpriteBatch batch, int screenWidth, int screenHeight)
    {
        _inner.DrawSniperScope(batch, screenWidth, screenHeight);
    }

    public void DrawBinocularOverlay(
        SpriteBatch batch,
        int screenWidth,
        int screenHeight,
        IReadOnlyList<EnemyOverlayBox>? targets,
        float headingRadians,
        int simulationTick,
        float? rangeMetres)
    {
        _inner.DrawBinocularOverlay(batch, screenWidth, screenHeight, targets, headingRadians, simulationTick, rangeMetres);
    }

    public void DrawAction(SpriteBatch batch, string spriteName, int screenWidth, int screenHeight, int scale)
    {
        _inner.DrawAction(batch, spriteName, screenWidth, screenHeight, scale);
    }

    public void DrawHandicamTimecode(SpriteBatch batch, CutsceneFrame frame, int screenWidth, int screenHeight)
    {
        _inner.DrawHandicamTimecode(batch, frame, screenWidth, screenHeight);
    }

    public void DrawStatusMessages(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float scale)
    {
        if (!_enabled.Value)
        {
            _inner.DrawStatusMessages(batch, messages, screenWidth, screenHeight, scale);
            return;
        }

        // 1. In-Game Messages: Handled natively by the game's HUD
        var nonCutscenes = messages.Where(m => !m.IsCutscene).ToList();
        if (nonCutscenes.Count > 0)
        {
            _inner.DrawStatusMessages(batch, nonCutscenes, screenWidth, screenHeight, scale);
        }

        // 2. Cutscenes: Render HD subtitles with active language in the cinematic bottom letterbox
        var cutscenes = messages.Where(m => m.IsCutscene).ToList();
        if (cutscenes.Count > 0)
        {
            DrawHdCutsceneSubtitles(batch, cutscenes, screenWidth, screenHeight);
        }
    }

    private void DrawHdCutsceneSubtitles(
        SpriteBatch batch,
        List<StatusMessageDisplay> cutsceneMessages,
        int screenWidth,
        int screenHeight)
    {
        var currentLang = _language.Value;
        var font = GetFontForLanguage(currentLang);

        if (font == null)
        {
            _statusRenderer.Draw(batch, cutsceneMessages, screenWidth, screenHeight, 0f);
            return;
        }

        var context = new LayoutContext(
            Math.Max(1, screenWidth),
            Math.Max(1, screenHeight),
            null,
            default,
            1f,
            1f);

        var painter = RhiPainterExtensions.Create(batch, context, null);

        float fontSize = context.Px(27f);
        float maxWidth = Math.Min(screenWidth * 0.75f, context.Px(1180f));
        float lineHeight = font.LineHeight(fontSize);

        var shadowColor = new Bgra32(0, 0, 0, 230);
        var shadowOffset = new UiVec2(context.Px(1.5f), context.Px(1.5f));
        var defaultTextColor = Bgra32.FromRgb(255, 255, 255);

        foreach (var msg in cutsceneMessages)
        {
            if (msg.RevealedCharacters <= 0 || string.IsNullOrWhiteSpace(msg.Text))
            {
                continue;
            }

            // Translate text according to selected language
            string rawText = msg.Text;
            string text = Translate(rawText, currentLang);

            var (speaker, speakerColor) = ParseSpeaker(text);

            string wrapped = font.WrapText(text, fontSize, maxWidth);
            string[] lines = wrapped.Split('\n');
            if (lines.Length == 0) continue;

            float totalHeight = lines.Length * lineHeight;

            // Center subtitle vertically in the cinematic bottom letterbox bar (~0.932f)
            float startY = (screenHeight * 0.932f) - (totalHeight * 0.5f);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                float currentY = startY + (i * lineHeight);

                if (i == 0 && speaker != null && line.StartsWith(speaker + ":", StringComparison.OrdinalIgnoreCase))
                {
                    string prefix = line.Substring(0, speaker.Length + 1);
                    string remainder = line.Substring(prefix.Length);

                    float prefixWidth = font.MeasureWidth(prefix.AsSpan(), fontSize);
                    float lineWidth = font.MeasureWidth(line.AsSpan(), fontSize);
                    float startX = (screenWidth - lineWidth) * 0.5f;

                    // 1. Draw speaker prefix with character color & drop shadow
                    painter.DrawTextShadowed(
                        font,
                        prefix.AsSpan(),
                        new UiVec2(startX, currentY),
                        fontSize,
                        speakerColor,
                        shadowColor,
                        shadowOffset);

                    // 2. Draw dialog body in crisp white & drop shadow
                    if (!string.IsNullOrEmpty(remainder))
                    {
                        painter.DrawTextShadowed(
                            font,
                            remainder.AsSpan(),
                            new UiVec2(startX + prefixWidth, currentY),
                            fontSize,
                            defaultTextColor,
                            shadowColor,
                            shadowOffset);
                    }
                }
                else
                {
                    float lineWidth = font.MeasureWidth(line.AsSpan(), fontSize);
                    float startX = (screenWidth - lineWidth) * 0.5f;

                    painter.DrawTextShadowed(
                        font,
                        line.AsSpan(),
                        new UiVec2(startX, currentY),
                        fontSize,
                        defaultTextColor,
                        shadowColor,
                        shadowOffset);
                }
            }
        }
    }

    #region Localization & Translation System

    private string Translate(string englishText, SubtitleLanguage language)
    {
        if (language == SubtitleLanguage.English)
        {
            return englishText;
        }

        if (_translations.TryGetValue(language, out var dict))
        {
            if (dict.TryGetValue(englishText.Trim(), out var translated))
            {
                return translated;
            }

            int colonIdx = englishText.IndexOf(':');
            if (colonIdx > 0 && colonIdx <= 25)
            {
                string speaker = englishText.Substring(0, colonIdx).Trim();
                string body = englishText.Substring(colonIdx + 1).Trim();
                if (dict.TryGetValue(body, out var translatedBody))
                {
                    return $"{speaker}: {translatedBody}";
                }
            }
        }

        return englishText;
    }

    private void InitializeBuiltInTranslations()
    {
        // Russian (Русский)
        var ru = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: Отличная работа, что добрался так быстро. Это задание имеет высший приоритет. Цель: Йозеф Прибой. Ты должен найти его и доставить для допроса. Йозеф — наша единственная зацепка в деле о краже боеголовки.",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "Отличная работа, что добрался так быстро. Это задание имеет высший приоритет. Цель: Йозеф Прибой. Ты должен найти его и доставить для допроса. Йозеф — наша единственная зацепка в деле о краже боеголовки.",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: Проникни на базу и скачай коды безопасности с компьютера.",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: Я на позиции. Жду указаний."
        };
        _translations[SubtitleLanguage.Russian] = ru;

        // Sinhala (සිංහල)
        var si = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: මෙතරම් ඉක්මනින් මෙහි පැමිණීම අගය කරනවා. මෙම මෙහෙයුමට ඉහළම ප්‍රමුඛතාවය හිමිවේ. ඉලක්කය: ජෝසෙෆ් ප්‍රිබෝයි. ඔහුව සොයාගෙන ප්‍රශ්න කිරීම සඳහා රැගෙන ආ යුතුය. යුධ හිස පැහැර ගැනීම පිළිබඳ අපගේ එකම සම්බන්ධය ජෝසෙෆ්ය.",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "මෙතරම් ඉක්මනින් මෙහි පැමිණීම අගය කරනවා. මෙම මෙහෙයුමට ඉහළම ප්‍රමුඛතාවය හිමිවේ. ඉලක්කය: ජෝසෙෆ් ප්‍රිබෝයි. ඔහුව සොයාගෙන ප්‍රශ්න කිරීම සඳහා රැගෙන ආ යුතුය. යුධ හිස පැහැර ගැනීම පිළිබඳ අපගේ එකම සම්බන්ධය ජෝසෙෆ්ය.",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: කඳවුරට ඇතුළු වී පරිගණකයෙන් ආරක්ෂක කේත ලබා ගන්න.",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: මම ස්ථානගතව සිටිමි. නියෝග බලාපොරොත්තුවෙන්."
        };
        _translations[SubtitleLanguage.Sinhala] = si;

        // Tamil (தமிழ்)
        var ta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: இவ்வளவு விரைவாக இங்கு வந்ததற்கு நன்று. இந்த பணிக்கு மிக உயர்ந்த முன்னுரிமை உள்ளது. இலக்கு: ஜோசப் ப்ரிபோய். நீங்கள் அவரைக் கண்டுபிடித்து விசாரணைக்கு அழைத்து வர வேண்டும். போர்த்தளவாட கடத்தலில் ஜோசப் மட்டுமே நமது ஒரே தொடர்பு.",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "இவ்வளவு விரைவாக இங்கு வந்ததற்கு நன்று. இந்த பணிக்கு மிக உயர்ந்த முன்னுரிமை உள்ளது. இலக்கு: ஜோசப் ப்ரிபோய். நீங்கள் அவரைக் கண்டுபிடித்து விசாரணைக்கு அழைத்து வர வேண்டும். போர்த்தளவாட கடத்தலில் ஜோசப் மட்டுமே நமது ஒரே தொடர்பு.",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: தளத்திற்குள் நுழைந்து கணினியிலிருந்து பாதுகாப்பு குறியீடுகளைப் பதிவிறக்கவும்.",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: நான் சரியான நிலையில் உள்ளேன். உத்தரவுகளுக்காக காத்திருக்கிறேன்."
        };
        _translations[SubtitleLanguage.Tamil] = ta;

        // Hindi (हिन्दी)
        var hi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: इतनी जल्दी यहाँ पहुँचने के लिए बहुत अच्छा काम। इस मिशन को सर्वोच्च प्राथमिकता दी गई है। लक्ष्य: जोसेफ प्रिबोई। आपको उसे ढूँढना है और पूछताछ के लिए बाहर लाना है। वॉरहेड की चोरी में जोसेफ ही हमारी एकमात्र कड़ी है।",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "इतनी जल्दी यहाँ पहुँचने के लिए बहुत अच्छा काम। इस मिशन को सर्वोच्च प्राथमिकता दी गई है। लक्ष्य: जोसेफ प्रिबोई। आपको उसे ढूँढना है और पूछताछ के लिए बाहर लाना है। वॉरहेड की चोरी में जोसेफ ही हमारी एकमात्र कड़ी है।",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: बेस में घुसपैठ करें और कंप्यूटर से सुरक्षा कोड डाउनलोड करें।",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: मैं अपनी स्थिति में हूँ। आदेशों की प्रतीक्षा कर रहा हूँ।"
        };
        _translations[SubtitleLanguage.Hindi] = hi;

        // Spanish (Español)
        var es = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: Buen trabajo llegando tan rápido. Esta misión tiene la máxima prioridad. Objetivo: Josef Priboi. Debes localizarlo y traerlo para interrogarlo. Josef es nuestro único vínculo con el robo de la ojiva.",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "Buen trabajo llegando tan rápido. Esta misión tiene la máxima prioridad. Objetivo: Josef Priboi. Debes localizarlo y traerlo para interrogarlo. Josef es nuestro único vínculo con el robo de la ojiva.",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: Infiltra la base y descarga los códigos de seguridad de la computadora.",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: Estoy en posición. Esperando órdenes."
        };
        _translations[SubtitleLanguage.Spanish] = es;

        // German (Deutsch)
        var de = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANYA: Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "ANYA: Gute Arbeit, dass Sie so schnell hier sind. Diese Mission hat höchste Priorität. Ziel: Josef Priboi. Sie müssen ihn lokalisieren und zum Verhör herbringen. Josef ist unsere einzige Verbindung zum Diebstahl des Sprengkopfs.",
            ["Good work getting here so fast. This mission has top priority. Target: Josef Priboi. You are to locate him and bring him out for debriefing. Josef is our only link with the heist of the warhead."] =
                "Gute Arbeit, dass Sie so schnell hier sind. Diese Mission hat höchste Priorität. Ziel: Josef Priboi. Sie müssen ihn lokalisieren und zum Verhör herbringen. Josef ist unsere einzige Verbindung zum Diebstahl des Sprengkopfs.",
            ["ANYA: Infiltrate the base and download the security codes from the computer."] =
                "ANYA: Infiltrieren Sie die Basis und laden Sie die Sicherheitscodes vom Computer herunter.",
            ["JONES: I'm in position. Awaiting orders."] =
                "JONES: Ich bin in Position. Erwarte Befehle."
        };
        _translations[SubtitleLanguage.German] = de;
    }

    private void LoadExternalTranslations()
    {
        try
        {
            string translationsDir = Path.Combine(_modDirectory, "translations");
            if (!Directory.Exists(translationsDir))
            {
                Directory.CreateDirectory(translationsDir);
                return;
            }

            foreach (var lang in Enum.GetValues<SubtitleLanguage>())
            {
                if (lang == SubtitleLanguage.English) continue;

                string filePath = Path.Combine(translationsDir, $"{lang.ToString().ToLowerInvariant()}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        if (!_translations.TryGetValue(lang, out var existing))
                        {
                            existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _translations[lang] = existing;
                        }

                        foreach (var kv in dict)
                        {
                            existing[kv.Key] = kv.Value;
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently continue if external translation file IO fails
        }
    }

    #endregion

    private static (string? speaker, Bgra32 color) ParseSpeaker(string text)
    {
        int colonIdx = text.IndexOf(':');
        if (colonIdx > 0 && colonIdx <= 25)
        {
            string candidate = text.Substring(0, colonIdx).Trim();
            if (candidate.All(c => char.IsLetter(c) || c == ' ' || c == '.' || c == '-'))
            {
                string upper = candidate.ToUpperInvariant();
                Bgra32 color = upper switch
                {
                    "ANYA" => Bgra32.FromRgb(0, 220, 255),                                 // Electric Cyan
                    "JONES" => Bgra32.FromRgb(80, 230, 110),                               // Tactical Green
                    "PRIBOI" or "JOSEF" or "JOSEF PRIBOI" => Bgra32.FromRgb(255, 210, 60),  // Warm Gold
                    "EKK" => Bgra32.FromRgb(255, 75, 75),                                  // Crimson Red
                    "HARRISON" => Bgra32.FromRgb(255, 170, 50),                            // Amber
                    _ => Bgra32.FromRgb(100, 215, 255)                                     // Sky Blue
                };

                return (candidate, color);
            }
        }

        return (null, Bgra32.FromRgb(255, 255, 255));
    }

    #region IHudIconSource (Restores full in-game weapon/item icons)

    public bool TryMeasure(HudIcon icon, out float width, out float height)
    {
        if (_inner is IHudIconSource iconSource)
        {
            return iconSource.TryMeasure(icon, out width, out height);
        }

        width = 0;
        height = 0;
        return false;
    }

    public void Draw(SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour)
    {
        if (_inner is IHudIconSource iconSource)
        {
            iconSource.Draw(batch, icon, x, y, scale, colour);
        }
    }

    public SpriteResource? Resolve(HudIcon icon)
    {
        if (_inner is IHudIconSource iconSource)
        {
            return iconSource.Resolve(icon);
        }

        return null;
    }

    #endregion

    public void Dispose()
    {
        (_inner as IDisposable)?.Dispose();
        _statusRenderer.Dispose();
    }
}
