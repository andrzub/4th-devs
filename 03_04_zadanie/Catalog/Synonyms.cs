namespace _03_04_zadanie.Catalog;

/// <summary>
/// The agent does not speak the catalog's dialect. Prefix matching already carries inflection
/// ("turbiny" → "turbina") and the shared Latin roots ("turbine" → "turbina"), so this table only
/// has to cover the words where the two languages genuinely diverge.
/// </summary>
public static class Synonyms
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {
        ["battery"] = ["akumulator"],
        ["batteries"] = ["akumulator"],
        ["bateria"] = ["akumulator"],
        ["baterie"] = ["akumulator"],
        ["akumulatorek"] = ["akumulator"],
        ["ogniwo"] = ["akumulator"],
        ["inverter"] = ["inwerter"],
        ["przetwornica"] = ["inwerter"],
        ["falownik"] = ["inwerter"],
        ["windmill"] = ["turbina", "wiatrowa"],
        ["wiatrak"] = ["turbina", "wiatrowa"],
        ["wind"] = ["wiatrowa"],
        ["wiatru"] = ["wiatrowa"],
        ["wiatrowy"] = ["wiatrowa"],
        ["resistor"] = ["rezystor"],
        ["capacitor"] = ["kondensator"],
        ["diode"] = ["dioda"],
        ["transistor"] = ["tranzystor"],
        ["sensor"] = ["czujnik"],
        ["relay"] = ["przekaznik"],
        ["fan"] = ["wentylator"],
        ["display"] = ["wyswietlacz"],
        ["screen"] = ["wyswietlacz"],
        ["fuse"] = ["bezpiecznik"],
        ["coil"] = ["cewka"],
        ["inductor"] = ["cewka"],
        ["connector"] = ["zlacze"],
        ["plug"] = ["zlacze"],
        ["switch"] = ["przelacznik"],
        ["button"] = ["przycisk"],
        ["heatsink"] = ["radiator"],
        ["amplifier"] = ["wzmacniacz"],
        ["microcontroller"] = ["mikrokontroler"],
        ["mcu"] = ["mikrokontroler"],
        ["antenna"] = ["antena"],
        ["potentiometer"] = ["potencjometr"],
        ["regulator"] = ["stabilizator"],
        ["crystal"] = ["rezonator"],
        ["oscillator"] = ["rezonator"],
        ["optocoupler"] = ["optoizolator"],
        ["module"] = ["modul"],
    };

    public static IReadOnlyList<string> Expand(IReadOnlyList<string> tokens)
    {
        var expanded = new List<string>(tokens.Count);

        foreach (var token in tokens)
        {
            if (Map.TryGetValue(token, out var replacements))
            {
                expanded.AddRange(replacements);
                continue;
            }

            expanded.Add(token);
        }

        return expanded;
    }
}
