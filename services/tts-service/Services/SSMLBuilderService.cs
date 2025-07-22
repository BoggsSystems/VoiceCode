using System.Text;
using System.Xml.Linq;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Services;

public class SSMLBuilderService : ISSMLBuilder
{
    private readonly ILogger<SSMLBuilderService> _logger;

    public SSMLBuilderService(ILogger<SSMLBuilderService> logger)
    {
        _logger = logger;
    }

    public Task<string> BuildAsync(string text, VoiceProfile profile, string? emotion = null)
    {
        try
        {
            var ssml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(XName.Get("speak", "http://www.w3.org/2001/10/synthesis"),
                    new XAttribute("version", "1.0"),
                    new XAttribute(XNamespace.Xml + "lang", profile.Language),
                    BuildVoiceElement(text, profile, emotion)
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

    private XElement BuildVoiceElement(string text, VoiceProfile profile, string? emotion)
    {
        var voiceElement = new XElement("voice",
            new XAttribute("name", profile.Voice));

        // Add style if available
        if (!string.IsNullOrEmpty(profile.Style.Style))
        {
            var styleElement = new XElement("mstts:express-as",
                new XAttribute(XNamespace.Xmlns + "mstts", "http://www.w3.org/2001/mstts"),
                new XAttribute("style", emotion ?? profile.Style.Style));

            if (profile.Style.StyleDegree != 1.0)
            {
                styleElement.Add(new XAttribute("styledegree", profile.Style.StyleDegree.ToString("F1")));
            }

            if (!string.IsNullOrEmpty(profile.Style.Role))
            {
                styleElement.Add(new XAttribute("role", profile.Style.Role));
            }

            // Add prosody element inside style
            var prosodyElement = BuildProsodyElement(text, profile.Prosody);
            styleElement.Add(prosodyElement);
            voiceElement.Add(styleElement);
        }
        else
        {
            // Add prosody directly if no style
            var prosodyElement = BuildProsodyElement(text, profile.Prosody);
            voiceElement.Add(prosodyElement);
        }

        return voiceElement;
    }

    private XElement BuildProsodyElement(string text, ProsodySettings prosody)
    {
        var prosodyElement = new XElement("prosody");

        if (prosody.Rate != "1.0")
        {
            prosodyElement.Add(new XAttribute("rate", prosody.Rate));
        }

        if (prosody.Pitch != "0%")
        {
            prosodyElement.Add(new XAttribute("pitch", prosody.Pitch));
        }

        if (prosody.Volume != "100")
        {
            prosodyElement.Add(new XAttribute("volume", prosody.Volume));
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

        // Add smart pauses after punctuation
        text = text.Replace(".", ".<break time=\"300ms\"/>");
        text = text.Replace("!", "!<break time=\"300ms\"/>");
        text = text.Replace("?", "?<break time=\"300ms\"/>");
        text = text.Replace(",", ",<break time=\"150ms\"/>");
        text = text.Replace(":", ":<break time=\"200ms\"/>");
        text = text.Replace(";", ";<break time=\"200ms\"/>");

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