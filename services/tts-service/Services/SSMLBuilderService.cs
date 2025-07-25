using System.Text;
using System.Xml.Linq;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Services.Interfaces;
using VoiceCode.Common.Models;
using Models = VoiceCode.Common.Models;

namespace VoiceCode.TTSService.Services;

public class SSMLBuilderService : ISSMLBuilder
{
    private readonly ILogger<SSMLBuilderService> _logger;

    public SSMLBuilderService(ILogger<SSMLBuilderService> logger)
    {
        _logger = logger;
    }

    public Task<string> BuildAsync(string text, VoiceCode.Common.Models.VoiceProfile profile, string? emotion = null)
    {
        try
        {
            XNamespace ns = "http://www.w3.org/2001/10/synthesis";
            XNamespace mstts = "http://www.w3.org/2001/mstts";
            
            var ssml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(ns + "speak",
                    new XAttribute("version", "1.0"),
                    new XAttribute(XNamespace.Xml + "lang", profile.Language),
                    new XAttribute(XNamespace.Xmlns + "mstts", mstts),
                    BuildVoiceElement(text, profile, emotion, mstts)
                )
            );

            return Task.FromResult(ssml.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building SSML");
            return Task.FromResult(text); // Fallback to plain text
        }
    }

    private XElement BuildVoiceElement(string text, VoiceCode.Common.Models.VoiceProfile profile, string? emotion, XNamespace mstts)
    {
        var voiceElement = new XElement("voice",
            new XAttribute("name", profile.NeuralVoiceName));

        // Add style if available
        if (profile.Styles != null && profile.Styles.Count > 0)
        {
            var style = emotion ?? (profile.Styles.ContainsKey("default") ? profile.Styles["default"] : profile.Styles.First().Value);
            var styleElement = new XElement(mstts + "express-as",
                new XAttribute("style", style));

            // Add prosody element inside style
            var prosodyElement = BuildProsodyElement(text, profile.Characteristics);
            styleElement.Add(prosodyElement);
            voiceElement.Add(styleElement);
        }
        else
        {
            // Add prosody directly if no style
            var prosodyElement = BuildProsodyElement(text, profile.Characteristics);
            voiceElement.Add(prosodyElement);
        }

        return voiceElement;
    }

    private XElement BuildProsodyElement(string text, VoiceCharacteristics characteristics)
    {
        var prosodyElement = new XElement("prosody");

        if (characteristics.DefaultSpeed != 1.0)
        {
            prosodyElement.Add(new XAttribute("rate", characteristics.DefaultSpeed.ToString("F1")));
        }

        if (characteristics.DefaultPitch != 1.0)
        {
            var pitchValue = ((characteristics.DefaultPitch - 1.0) * 100).ToString("F0") + "%";
            prosodyElement.Add(new XAttribute("pitch", pitchValue));
        }

        // Process text for smart pauses and emphasis
        var processedText = ProcessTextForSSML(text);
        prosodyElement.Add(new XText(processedText));

        return prosodyElement;
    }

    private string ProcessTextForSSML(string text)
    {
        // Escape XML special characters
        text = System.Security.SecurityElement.Escape(text);
        
        // For now, just return the escaped text without adding break tags
        // as they would need to be proper XML elements, not text
        return text;
    }

    public string AddEmphasis(string text, string level = "moderate")
    {
        return $"<emphasis level=\"{level}\">{System.Security.SecurityElement.Escape(text)}</emphasis>";
    }

    public string AddPause(string duration = "500ms")
    {
        return $"<break time=\"{duration}\"/>";
    }

    public string AddProsody(string text, string rate = "1.0", string pitch = "0%", string volume = "100")
    {
        var prosody = new StringBuilder("<prosody");
        
        if (rate != "1.0") prosody.Append($" rate=\"{rate}\"");
        if (pitch != "0%") prosody.Append($" pitch=\"{pitch}\"");
        if (volume != "100") prosody.Append($" volume=\"{volume}\"");
        
        prosody.Append($">{System.Security.SecurityElement.Escape(text)}</prosody>");
        
        return prosody.ToString();
    }
}