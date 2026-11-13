using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

public sealed class Mod : IOpenIgiMod
{
    public void Configure(IModBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        BoolCVar enabled = builder.Settings.Bool("enabled", true, "Enable modern HD cutscene subtitles.");

        builder.Presenters(presenters =>
        {
            presenters.Replace<HudPresentationRequest, IHudPresenter>((in HudPresentationRequest request) =>
            {
                var asm = Assembly.Load("OpenIGI.Presentation.Igi1");
                var ssType = asm.GetType("OpenIGI.Presentation.Igi1.World.StatusScreenRenderer")!;
                var original = (IHudPresenter)Activator.CreateInstance(ssType, request.Device, request.Logger)!;
                var hdStatusRenderer = new StatusMessageRenderer(request.Device, request.Logger);

                return new CustomHudPresenter(original, hdStatusRenderer, request.Device, enabled);
            });
        });
    }
}

/// <summary>
/// Wraps the HUD presenter:
/// - Implements IHudIconSource to preserve full in-game HUD rendering (weapons, ammo, health, radar).
/// - Forwards in-game messages directly to the native HUD so gameplay is 100% untouched.
/// - Upgrades cutscene subtitles to modern HD TrueType typography, positioned cleanly in the bottom black letterbox, with speaker color highlights.
/// </summary>
public sealed class CustomHudPresenter : IHudPresenter, IHudIconSource, IDisposable
{
    private readonly IHudPresenter _inner;
    private readonly StatusMessageRenderer _statusRenderer;
    private readonly IGraphicsDevice _device;
    private readonly BoolCVar _enabled;

    private UiFont? _font;
    private readonly FieldInfo? _bodyField;

    public CustomHudPresenter(
        IHudPresenter inner,
        StatusMessageRenderer statusRenderer,
        IGraphicsDevice device,
        BoolCVar enabled)
    {
        _inner = inner;
        _statusRenderer = statusRenderer;
        _device = device;
        _enabled = enabled;
        _bodyField = typeof(StatusMessageRenderer).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public bool IsReady => _inner.IsReady && _statusRenderer.IsReady;

    public bool Load(VirtualFileSystem fileSystem)
    {
        bool ok = _inner.Load(fileSystem);
        _statusRenderer.Load();
        EnsureFont();
        return ok;
    }

    private void EnsureFont()
    {
        if (_font != null) return;

        if (_bodyField != null)
        {
            _font = (UiFont?)_bodyField.GetValue(_statusRenderer);
        }

        if (_font == null && _device != null && UiFontLibrary.TypographyAvailable)
        {
            try
            {
                _font = UiFontLibrary.Typography(_device).Body;
            }
            catch
            {
                // Ignored - will fallback if unavailable
            }
        }
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

        // 2. Cutscenes: Render HD subtitles with speaker color theme positioned in bottom black letterbox
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
        EnsureFont();

        // If HD vector font is not yet available, fallback to StatusMessageRenderer
        if (_font == null)
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
        float lineHeight = _font.LineHeight(fontSize);

        var shadowColor = new Bgra32(0, 0, 0, 230);
        var shadowOffset = new UiVec2(context.Px(1.5f), context.Px(1.5f));
        var defaultTextColor = Bgra32.FromRgb(255, 255, 255);

        foreach (var msg in cutsceneMessages)
        {
            if (msg.RevealedCharacters <= 0 || string.IsNullOrWhiteSpace(msg.Text))
            {
                continue;
            }

            string text = msg.Text;
            var (speaker, speakerColor) = ParseSpeaker(text);

            // Wrap text according to maxWidth
            string wrapped = _font.WrapText(text, fontSize, maxWidth);
            string[] lines = wrapped.Split('\n');
            if (lines.Length == 0) continue;

            float totalHeight = lines.Length * lineHeight;

            // Center subtitle vertically in the cinematic bottom letterbox bar:
            // Standard letterbox occupies ~0.865f to 1.0f of screenHeight; center is at 0.932f.
            float startY = (screenHeight * 0.932f) - (totalHeight * 0.5f);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                float currentY = startY + (i * lineHeight);

                if (i == 0 && speaker != null && line.StartsWith(speaker + ":", StringComparison.OrdinalIgnoreCase))
                {
                    // Line 0 contains the speaker prefix (e.g. "ANYA:")
                    string prefix = line.Substring(0, speaker.Length + 1); // includes ":"
                    string remainder = line.Substring(prefix.Length);

                    float prefixWidth = _font.MeasureWidth(prefix.AsSpan(), fontSize);
                    float lineWidth = _font.MeasureWidth(line.AsSpan(), fontSize);
                    float startX = (screenWidth - lineWidth) * 0.5f;

                    // 1. Draw speaker prefix with character color & drop shadow
                    painter.DrawTextShadowed(
                        _font,
                        prefix.AsSpan(),
                        new UiVec2(startX, currentY),
                        fontSize,
                        speakerColor,
                        shadowColor,
                        shadowOffset);

                    // 2. Draw dialog text with crisp white & drop shadow
                    if (!string.IsNullOrEmpty(remainder))
                    {
                        painter.DrawTextShadowed(
                            _font,
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
                    // Subsequent lines: centered dialog text with crisp white & drop shadow
                    float lineWidth = _font.MeasureWidth(line.AsSpan(), fontSize);
                    float startX = (screenWidth - lineWidth) * 0.5f;

                    painter.DrawTextShadowed(
                        _font,
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
