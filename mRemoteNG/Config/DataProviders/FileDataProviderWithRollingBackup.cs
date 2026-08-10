using System;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.DataProviders;

[SupportedOSPlatform("windows")]
public class FileDataProviderWithRollingBackup(string filePath) : FileDataProvider(filePath)
{
    private readonly FileBackupCreator _fileBackupCreator = new FileBackupCreator();

    public override void Save(string content)
    {
        FileBackupCreator.CreateBackupFile(FilePath);
        base.Save(content);
    }

    /// <summary>
    /// This provider writes the connection file, so a failure here means the user's change
    /// was not stored. Logging it and returning normally told the caller the save succeeded,
    /// which is how a change could disappear with nothing said about it.
    /// </summary>
    protected override void HandleSaveException(Exception ex)
    {
        base.HandleSaveException(ex);
        ExceptionDispatchInfo.Capture(ex).Throw();
    }
}