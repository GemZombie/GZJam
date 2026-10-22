namespace GZJam.Audio;

/// <summary>
/// Names for Linux outputs that say what they are: ALSA's own are often cryptic ("sof-hda-dsp: ",
/// with nothing after the colon). Sound drivers name things very differently, so only what the
/// kernel reports for every card is used: whether it's plugged in by USB, and whether an output is
/// HDMI, digital or analog. Anything that doesn't fit keeps the name ALSA gave it.
/// </summary>
internal static class LinuxOutputNames
{
    /// <summary>What the kernel says about one output (ALSA's hw:card,device).</summary>
    /// <param name="CardName">The card's name, e.g. "Cable Creation" or "HDA Intel PCH".</param>
    /// <param name="Id">The output's id, e.g. "HDA Analog (*)" or "ALC892 Analog".</param>
    /// <param name="Name">The output's name, often blank or the same as the id.</param>
    /// <param name="Usb">The card is plugged in by USB.</param>
    /// <param name="OutputsOnCard">How many outputs the card has.</param>
    internal sealed record Output(string CardName, string Id, string Name, bool Usb, int OutputsOnCard);

    /// <summary>The name to show, or null to leave the output out of the list.</summary>
    /// <param name="systemName">ALSA's name, as BASS reports it: "card: output".</param>
    /// <param name="driver">BASS's driver: "default", "pipewire", "hw:1,0", ...</param>
    /// <param name="describe">Looks up an output by card and device number.</param>
    internal static string? For(string systemName, string driver, Func<int, int, Output?> describe)
    {
        // The sound server's own entry plays wherever "Default" does; one of the two is enough.
        if (driver is "pipewire" or "pulse")
            return null;
        if (!TryParseHw(driver, out int card, out int device) || describe(card, device) is not { } output)
            return systemName;

        string label = (output.Name.Trim().Length > 0 ? output.Name : output.Id).Replace("(*)", "").Trim();

        // Intel's Sound Open Firmware offers the analog jack twice, the second with a longer buffer.
        if (label.Contains("Deep Buffer", StringComparison.OrdinalIgnoreCase))
            return null;

        if (output.Usb)
        {
            string name = output.CardName.Contains("USB", StringComparison.OrdinalIgnoreCase)
                ? output.CardName
                : $"{output.CardName} (USB)";
            bool saysMore = label.Length > 0 && !label.Equals("USB Audio", StringComparison.OrdinalIgnoreCase);
            return output.OutputsOnCard > 1 && saysMore ? $"{name}: {label}" : name;
        }

        return "Built-in: " + Kind(label, output.CardName);
    }

    private static string Kind(string label, string cardName)
    {
        string both = $"{cardName} {label}";
        if (Has(both, "HDMI") || Has(both, "DisplayPort"))
            return Has(label, "HDMI") || Has(label, "DisplayPort") ? label : "HDMI";
        if (Has(label, "Digital") || Has(label, "SPDIF") || Has(label, "IEC958"))
            return "Digital out";
        if (label.Length == 0 || Has(label, "Analog"))
            return "Headphones / Speakers";
        return label;
    }

    private static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseHw(string driver, out int card, out int device)
    {
        card = device = 0;
        if (!driver.StartsWith("hw:", StringComparison.Ordinal))
            return false;
        string[] parts = driver[3..].Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out card) && int.TryParse(parts[1], out device);
    }

    /// <summary>Reads an output's description from /proc/asound; null if it isn't there.</summary>
    internal static Output? Describe(int card, int device)
    {
        try
        {
            string cardDir = $"/proc/asound/card{card}";
            string info = $"{cardDir}/pcm{device}p/info";
            if (!File.Exists(info))
                return null;
            string[] lines = File.ReadAllLines(info);
            return new Output(
                CardName(card) ?? "",
                Field(lines, "id"),
                Field(lines, "name"),
                File.Exists($"{cardDir}/usbid"),
                Directory.GetDirectories(cardDir, "pcm*p").Length);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Field(string[] lines, string key) =>
        lines.FirstOrDefault(l => l.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..] ?? "";

    /// <summary>The card's name from /proc/asound/cards: " 1 [Creation ]: USB-Audio - Cable Creation".</summary>
    private static string? CardName(int card)
    {
        foreach (string line in File.ReadLines("/proc/asound/cards"))
        {
            string trimmed = line.TrimStart();
            int bracket = trimmed.IndexOf(" [", StringComparison.Ordinal);
            int dash = trimmed.IndexOf(" - ", StringComparison.Ordinal);
            if (bracket > 0 && dash > bracket && int.TryParse(trimmed[..bracket], out int number) && number == card)
                return trimmed[(dash + 3)..].Trim();
        }
        return null;
    }
}
