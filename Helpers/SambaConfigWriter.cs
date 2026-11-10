using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAMBA_Util.Helpers;

public static class SambaConfigWriter
{
    private static long _callCountSave = 0;

    // Secciones que SAMBA-Util nunca escribe ni elimina: se conservan tal cual
    // estén en smb.conf. [homes] se ignora al leer (no se puede editar desde la UI),
    // así que debe preservarse aquí para no perderla al guardar.
    private static bool IsSpecialShare(string name) => SambaSections.IsSpecial(name);

    private static void ValidateShareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Share name cannot be empty.");

        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-$ ]+$"))
            throw new InvalidOperationException(
                $"Invalid share name '{name}'. Allowed characters: letters, numbers, space, dot, underscore, dash, dollar."
            );

        if (name.Equals("global", StringComparison.OrdinalIgnoreCase) ||
            IsSpecialShare(name))
        {
            throw new InvalidOperationException($"Share name '{name}' is reserved.");
        }
    }

    public static void SaveAllShares(IEnumerable<Share> uiShares)
    {
        ArgumentNullException.ThrowIfNull(uiShares);
        var sharesToSave = uiShares.ToList();
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares iniciado. Shares: {sharesToSave.Count}");

        foreach (var share in sharesToSave)
            ValidateShareName(share.Name);

        if (sharesToSave
            .GroupBy(share => share.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("La lista contiene nombres de recursos compartidos duplicados.");
        }

        var config = ConfigManager.Load();
        string smbConf = config.SmbConfPath;
        if (string.IsNullOrWhiteSpace(smbConf))
            throw new InvalidOperationException("La ruta del archivo smb.conf no está configurada.");

        string smbConfFull = Path.GetFullPath(smbConf);

        // Solo se reescriben los shares que viven en smb.conf. Los que vienen de
        // un include se quedan en su archivo y nunca se duplican aquí.
        var managedShares = sharesToSave
            .Where(share => !IsSpecialShare(share.Name))
            .Where(share => string.IsNullOrWhiteSpace(share.SourceFile) ||
                            PathsEqual(share.SourceFile, smbConfFull))
            .ToList();

        var skipped = sharesToSave.Except(managedShares).ToList();
        if (skipped.Count > 0)
        {
            Console.WriteLine(
                $"[WRITE] #{callId} Omitiendo {skipped.Count} share(s) definidos en includes: " +
                string.Join(", ", skipped.Select(s => $"{s.Name} ({s.SourceFile})")));
        }

        var originalLines = File.Exists(smbConfFull)
            ? File.ReadAllLines(smbConfFull).ToList()
            : new List<string>();

        var blocks = SplitIntoBlocks(originalLines);
        var managedByName = managedShares.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var output = BuildConfigLines(blocks, managedByName, managedShares);

        WriteConfig(output, smbConfFull, callId);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completado en {sw.ElapsedMilliseconds} ms");
    }

    // Valida con testparm, hace backup, copia el archivo y reinicia Samba.
    // Se usa tanto al guardar shares como al cambiar opciones de [global].
    private static void WriteConfig(List<string> output, string smbConfFull, long callId)
    {
        string backup = smbConfFull + ".bak";
        string temp = Path.Combine(Path.GetTempPath(), $"samba-util-{Guid.NewGuid():N}.conf");

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                foreach (var line in output)
                    writer.WriteLine(line);
            }

            var test = ShellHelper.EjecutarComoRoot($"testparm -s {ShellQuote(temp)}");
            if (test.ExitCode != 0)
            {
                Console.WriteLine($"[WRITE] #{callId} ERROR: testparm falló. smb.conf inválido.");
                Console.WriteLine(test.Stderr);
                throw new InvalidOperationException(
                    $"La configuración de Samba generada no es válida: {test.Stderr}{test.Stdout}");
            }

            if (File.Exists(smbConfFull))
            {
                var backupResult = ShellHelper.EjecutarComoRoot(
                    $"cp -p {ShellQuote(smbConfFull)} {ShellQuote(backup)}");
                if (backupResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"No se pudo crear el backup de smb.conf: {backupResult.Stderr}");
                }
            }

            var copyResult = ShellHelper.EjecutarComoRoot(
                $"cp {ShellQuote(temp)} {ShellQuote(smbConfFull)}");
            if (copyResult.ExitCode != 0)
            {
                Console.WriteLine($"[WRITE] #{callId} ERROR al copiar smb.conf");
                throw new InvalidOperationException(
                    $"No se pudo escribir el archivo smb.conf: {copyResult.Stderr}");
            }

            RestartSambaService();
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // Escribe "server min protocol" en [global] de smb.conf para que el ajuste
    // también se aplique al servidor Samba, no solo al cliente smbclient.
    // Devuelve true si el archivo cambió; false si ya tenía el valor pedido.
    public static bool SetServerMinProtocol(string? protocol)
    {
        var normalized = ConfigManager.NormalizeProtocol(protocol);
        var callId = ++_callCountSave;

        var config = ConfigManager.Load();
        string smbConf = config.SmbConfPath;
        if (string.IsNullOrWhiteSpace(smbConf))
            throw new InvalidOperationException("La ruta del archivo smb.conf no está configurada.");

        string smbConfFull = Path.GetFullPath(smbConf);
        var originalLines = File.Exists(smbConfFull)
            ? File.ReadAllLines(smbConfFull).ToList()
            : new List<string>();

        var output = BuildServerMinProtocolLines(originalLines, normalized);

        if (originalLines.SequenceEqual(output))
        {
            Console.WriteLine($"[WRITE] #{callId} server min protocol ya es {normalized}; no se reescribe smb.conf.");
            return false;
        }

        WriteConfig(output, smbConfFull, callId);

        var effective = GetEffectiveServerMinProtocol(smbConfFull);
        if (effective is not null &&
            !string.Equals(effective, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"smb.conf se escribió, pero testparm sigue reportando " +
                $"'{effective}' como server min protocol (esperado '{normalized}').");
        }

        Console.WriteLine($"[WRITE] #{callId} server min protocol = {normalized} aplicado y verificado.");
        return true;
    }

    // Valor que Samba aplica realmente para "server min protocol", según testparm.
    public static string? GetEffectiveServerMinProtocol(string? smbConfPath = null)
    {
        string path = smbConfPath ?? ConfigManager.Load().SmbConfPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var result = ShellHelper.EjecutarComoRoot(
            $"testparm -s --parameter-name=\"server min protocol\" {ShellQuote(path)}");
        if (result.ExitCode != 0)
            return null;

        // testparm imprime el valor en stdout; avisos como "Weak crypto..." van a
        // stderr, así que se busca el token de protocolo en stdout y, si no
        // aparece, se revisa también stderr como respaldo.
        var pattern = @"^\s*(NT1|SMB2(?:_\d+)?|SMB3(?:_\d+)?)\s*$";
        var match = Regex.Match(result.Stdout, pattern,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(result.Stdout + "\n" + result.Stderr, pattern,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static readonly Regex ServerMinProtocolRegex = new(
        @"^\s*server\s+min\s+protocol\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Sustituye/inserta la directiva en [global] y elimina duplicados,
    // conservando el resto del archivo tal cual.
    private static List<string> BuildServerMinProtocolLines(List<string> originalLines, string protocol)
    {
        var blocks = SplitIntoBlocks(originalLines);
        var output = new List<string>();
        bool globalFound = false;

        foreach (var block in blocks)
        {
            if (block.Name is null)
            {
                output.AddRange(block.Lines);
                continue;
            }

            if (!block.Name.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                output.AddRange(block.Lines);
                continue;
            }

            globalFound = true;
            bool written = false;
            foreach (var line in block.Lines)
            {
                if (ServerMinProtocolRegex.IsMatch(line))
                {
                    if (!written)
                    {
                        output.Add($"    server min protocol = {protocol}");
                        written = true;
                    }
                    // Duplicados: se descartan.
                    continue;
                }

                output.Add(line);

                // Insertar justo después de la cabecera [global] si no existía.
                if (!written && line.TrimStart().StartsWith('['))
                {
                    output.Add($"    server min protocol = {protocol}");
                    written = true;
                }
            }
        }

        if (!globalFound)
        {
            output.Insert(0, "");
            output.Insert(0, $"    server min protocol = {protocol}");
            output.Insert(0, "[global]");
        }

        return output;
    }

    public static void AddShare(Share newShare)
    {
        ValidateShareName(newShare.Name);
        var shares = SambaConfigReader.LoadShares();

        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Ya existe un recurso compartido llamado '{newShare.Name}'.");

        shares.Add(newShare);
        SaveAllShares(shares);
    }

    public static void DeleteShare(string name)
    {
        var shares = SambaConfigReader.LoadShares();
        var target = shares.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (target is not null)
            EnsureShareIsEditable(target);

        var filtered = shares.Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        SaveAllShares(filtered);
    }

    public static void UpdateShare(Share updated)
    {
        ValidateShareName(updated.Name);
        var shares = SambaConfigReader.LoadShares();

        var existing = shares.FirstOrDefault(s => s.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            EnsureShareIsEditable(existing);

            // El share pasa a estar gestionado desde smb.conf.
            updated.SourceFile = existing.SourceFile;
        }

        var list = shares.Where(s => !s.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(updated);
        SaveAllShares(list);
    }

    // Un share definido en un archivo incluido no se puede editar ni borrar
    // desde aquí: hacerlo lo duplicaría o lo dejaría huérfano en el include.
    public static bool IsFromInclude(Share share)
    {
        if (string.IsNullOrWhiteSpace(share.SourceFile))
            return false;

        string smbConfFull = Path.GetFullPath(ConfigManager.Load().SmbConfPath);
        return !PathsEqual(share.SourceFile, smbConfFull);
    }

    private static void EnsureShareIsEditable(Share share)
    {
        if (!IsFromInclude(share))
            return;

        string smbConfFull = Path.GetFullPath(ConfigManager.Load().SmbConfPath);
        throw new InvalidOperationException(
            $"El recurso compartido '{share.Name}' está definido en '{share.SourceFile}' " +
            $"(incluido desde {smbConfFull}). Edita ese archivo directamente o elimina la " +
            "directiva include antes de modificarlo desde SAMBA-Util.");
    }

    private sealed class SectionBlock
    {
        public string? Name { get; init; }
        public List<string> Lines { get; } = new();
    }

    // Divide el archivo en bloques por sección conservando todas las líneas
    // originales (comentarios, includes, orden y formato).
    private static List<SectionBlock> SplitIntoBlocks(List<string> lines)
    {
        var blocks = new List<SectionBlock>();
        var preamble = new SectionBlock { Name = null };
        var current = preamble;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            int close = trimmed.StartsWith('[') ? trimmed.IndexOf(']') : -1;

            if (close > 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith(';'))
            {
                var name = trimmed[1..close].Trim();
                current = new SectionBlock { Name = name };
                blocks.Add(current);
                current.Lines.Add(line);
                continue;
            }

            current.Lines.Add(line);
        }

        if (preamble.Lines.Any(line => !string.IsNullOrWhiteSpace(line)))
            blocks.Insert(0, preamble);

        return blocks;
    }

    private static void AddShareBlock(List<string> output, Share share)
    {
        if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
            output.Add("");

        output.AddRange(ShareToLines(share));
        output.Add("");
    }

    // Construye el contenido final conservando [global], includes, comentarios y
    // secciones especiales; reemplaza/elimina/añade solo los shares gestionados.
    private static List<string> BuildConfigLines(
        List<SectionBlock> blocks,
        Dictionary<string, Share> managedByName,
        List<Share> managedShares)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        bool globalFound = false;

        foreach (var block in blocks)
        {
            if (block.Name is null)
            {
                // Preámbulo (comentarios y directivas antes de la primera sección).
                output.AddRange(block.Lines);
                continue;
            }

            if (block.Name.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                globalFound = true;
                output.AddRange(block.Lines);
                continue;
            }

            if (IsSpecialShare(block.Name))
            {
                // [homes], [printers], etc.: se conservan verbatim.
                output.AddRange(block.Lines);
                continue;
            }

            if (managedByName.TryGetValue(block.Name, out var share))
            {
                AddShareBlock(output, share);
                written.Add(share.Name);
            }
            // Si no está en la lista de la UI, el usuario lo eliminó: se descarta.
        }

        foreach (var share in managedShares)
        {
            if (written.Contains(share.Name))
                continue;

            AddShareBlock(output, share);
            written.Add(share.Name);
        }

        if (!globalFound)
        {
            output.Insert(0, "[global]");
            if (output.Count > 1)
                output.Insert(1, "");
        }

        return output;
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return string.Equals(first, second, StringComparison.Ordinal);
        }
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static IEnumerable<string> ShareToLines(Share s)
    {
        yield return $"[{s.Name}]";
        yield return $"    path = {CleanPath(s.Path)}";
        yield return $"    read only = {(s.ReadOnly ? "yes" : "no")}";
        yield return $"    guest ok = {(s.AllowGuests ? "yes" : "no")}";
        yield return $"    browseable = {(s.Browseable ? "yes" : "no")}";
        yield return $"    follow symlinks = {(s.FollowSymlinks ? "yes" : "no")}";
        yield return $"    wide links = {(s.WideLinks ? "yes" : "no")}";

        if (!string.IsNullOrWhiteSpace(s.Comment))
            yield return $"    comment = {s.Comment}";

        if (!string.IsNullOrWhiteSpace(s.ValidUsers))
            yield return $"    valid users = {s.ValidUsers}";

        if (!string.IsNullOrWhiteSpace(s.WriteList))
            yield return $"    write list = {s.WriteList}";

        if (!string.IsNullOrWhiteSpace(s.ReadList))
            yield return $"    read list = {s.ReadList}";

        if (!string.IsNullOrWhiteSpace(s.ForceUser))
            yield return $"    force user = {s.ForceUser}";

        if (!string.IsNullOrWhiteSpace(s.ForceGroup))
            yield return $"    force group = {s.ForceGroup}";

        if (!string.IsNullOrWhiteSpace(s.CreateMask))
            yield return $"    create mask = {s.CreateMask}";

        if (!string.IsNullOrWhiteSpace(s.DirectoryMask))
            yield return $"    directory mask = {s.DirectoryMask}";

        if (s.UnknownParameters != null)
        {
            foreach (var param in s.UnknownParameters)
            {
                if (!string.IsNullOrWhiteSpace(param))
                    yield return $"    {param}";
            }
        }
    }

    private static string CleanPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Trim().Trim('"');
    }

    private static void RestartSambaService()
    {
        if (File.Exists("/bin/systemctl") || File.Exists("/usr/bin/systemctl"))
        {
            var res = ShellHelper.EjecutarComoRoot("systemctl restart smb");
            if (res.ExitCode != 0)
            {
                res = ShellHelper.EjecutarComoRoot("systemctl restart smbd");
            }

            if (res.ExitCode != 0)
                throw new InvalidOperationException($"No se pudo reiniciar Samba: {res.Stderr}");

            return;
        }

        if (File.Exists("/sbin/service") || File.Exists("/usr/sbin/service"))
        {
            var res = ShellHelper.EjecutarComoRoot("service smb restart");
            if (res.ExitCode != 0)
            {
                res = ShellHelper.EjecutarComoRoot("service smbd restart");
            }

            if (res.ExitCode != 0)
                throw new InvalidOperationException($"No se pudo reiniciar Samba: {res.Stderr}");

            return;
        }

        if (File.Exists("/sbin/rc-service"))
        {
            var res = ShellHelper.EjecutarComoRoot("rc-service samba restart");
            if (res.ExitCode != 0)
                throw new InvalidOperationException($"No se pudo reiniciar Samba: {res.Stderr}");

            return;
        }

        throw new InvalidOperationException("No se pudo detectar el gestor de servicios para reiniciar Samba.");
    }
}
