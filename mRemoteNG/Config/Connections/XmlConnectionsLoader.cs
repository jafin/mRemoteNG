using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Xml;
using mRemoteNG.App;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Messages;
using mRemoteNG.Security.FileProtection;
using mRemoteNG.Tools;
using mRemoteNG.Tree;

namespace mRemoteNG.Config.Connections;

[SupportedOSPlatform("windows")]
public class XmlConnectionsLoader : IConnectionsLoader
{
    private readonly string _connectionFilePath;
    private readonly MessageCollector _messageCollector;
    private readonly Func<string, Optional<SecureString>> _passwordRequestor;

    public XmlConnectionsLoader(string connectionFilePath, MessageCollector? messageCollector = null)
        : this(connectionFilePath, messageCollector,
            (fileNameToPrompt) => MiscTools.PasswordDialog(Path.GetFileName(connectionFilePath), false))
    {
    }

    public XmlConnectionsLoader(string connectionFilePath, MessageCollector? messageCollector,
        Func<string, Optional<SecureString>> passwordRequestor)
    {
        if (string.IsNullOrEmpty(connectionFilePath))
            throw new ArgumentException($"{nameof(connectionFilePath)} cannot be null or empty", nameof(connectionFilePath));

        if (!File.Exists(connectionFilePath))
            throw new FileNotFoundException($"{connectionFilePath} does not exist");

        _connectionFilePath = connectionFilePath;
        _messageCollector = messageCollector ?? Runtime.MessageCollector;
        _passwordRequestor = passwordRequestor;
    }

    public ConnectionTreeModel Load()
    {
        FileDataProvider dataProvider = new(_connectionFilePath);
        string xmlString = dataProvider.Load();
        XmlConnectionsDeserializer deserializer = new(_connectionFilePath, () => PromptForPassword());

        // An empty file may indicate crash-time corruption (#1395).
        // Deserialize() returns null silently for empty input, so we must intercept here
        // before backup recovery is bypassed.
        if (string.IsNullOrWhiteSpace(xmlString))
        {
            _messageCollector.AddMessage(MessageClass.WarningMsg,
                $"Connection file '{_connectionFilePath}' is empty. Attempting backup recovery.");

            if (TryRecoverFromBackup(deserializer, out ConnectionTreeModel? recovered))
                return recovered!;

            throw new XmlException($"Connection file '{_connectionFilePath}' is empty and no valid backup was found.");
        }

        try
        {
            return deserializer.Deserialize(xmlString);
        }
        catch (KeyProtectionException ex)
        {
            // Deliberately ahead of the recovery clause below, and deliberately not folded into it.
            // A key that cannot be unwrapped is not a corrupt file: every backup is a byte-identical
            // copy carrying the same protectors, so searching them would fail identically for each
            // one — a warning per backup implying the whole set is corrupt when the files are
            // intact, and up to three password prompts each.
            //
            // This being a separate clause rather than an absence from the filter is the point. The
            // filter used to exclude it only by not naming it, so widening that list for an
            // unrelated reason would have quietly turned a wrong password into a walk of the backup
            // set — ending in File.Copy over the live file.
            _messageCollector.AddExceptionMessage(
                $"The key for connection file '{_connectionFilePath}' could not be unwrapped. Its backups " +
                "carry the same protection, so they were not searched and the file was left unchanged.",
                ex,
                MessageClass.ErrorMsg);

            throw;
        }
        catch (Exception ex) when (ex is XmlException or FormatException or InvalidOperationException or NotSupportedException)
        {
            _messageCollector.AddExceptionMessage(
                $"Failed to parse XML connection file '{_connectionFilePath}'. Attempting backup recovery.",
                ex,
                MessageClass.WarningMsg);

            if (TryRecoverFromBackup(deserializer, out ConnectionTreeModel? recoveredTreeModel))
                return recoveredTreeModel!;

            throw;
        }
    }

    private bool TryRecoverFromBackup(XmlConnectionsDeserializer deserializer, out ConnectionTreeModel? recoveredTreeModel)
    {
        recoveredTreeModel = null;
        string? directoryPath = Path.GetDirectoryName(_connectionFilePath);

        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        string fileName = Path.GetFileName(_connectionFilePath);
        string backupPattern = $"{fileName}.*.backup";
        string[] backupFiles = Directory.GetFiles(directoryPath, backupPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        foreach (string backupFile in backupFiles)
        {
            try
            {
                FileDataProvider backupDataProvider = new(backupFile);
                string backupXml = backupDataProvider.Load();
                ConnectionTreeModel backupTreeModel = deserializer.Deserialize(backupXml);
                if (backupTreeModel == null)
                    continue;

                File.Copy(backupFile, _connectionFilePath, overwrite: true);
                _messageCollector.AddMessage(MessageClass.WarningMsg, $"Recovered connection file from backup '{backupFile}'.");

                recoveredTreeModel = backupTreeModel;
                return true;
            }
            catch (KeyProtectionException ex)
            {
                // Stops the walk rather than moving to the next file. The backups are copies of the
                // same encrypted bytes, so the protector that just failed is the protector every one
                // of them carries; continuing would ask for the same password again for each, and
                // report each intact file as though it were damaged.
                //
                // One exception to state honestly: a backup taken before the recovery password was
                // last replaced carries the older protector, so stopping here can skip a file the
                // user could have opened with a different password. That is worth the trade — the
                // alternative is three prompts per backup for a set that is usually ten deep — and
                // it is why the message names the password rather than the file.
                _messageCollector.AddExceptionMessage(
                    $"Backup '{backupFile}' could not be unwrapped, and the rest carry the same " +
                    "protection, so the search stopped. The connection file has not been changed. If " +
                    "the recovery password was changed after a backup was taken, that backup needs " +
                    "the password that was in use when it was written.",
                    ex,
                    MessageClass.ErrorMsg);

                return false;
            }
            catch (Exception ex)
            {
                _messageCollector.AddExceptionMessage(
                    $"Failed to recover connections from backup '{backupFile}'.",
                    ex,
                    MessageClass.WarningMsg);
            }
        }

        return false;
    }

    private Optional<SecureString> PromptForPassword()
    {
        return _passwordRequestor(Path.GetFileName(_connectionFilePath));
    }
}