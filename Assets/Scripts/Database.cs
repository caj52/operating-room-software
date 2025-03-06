using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SplenSoft.UnityUtilities;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

internal static class Database
{
    private const string _cacheFolderName = "DatabaseCache";
    private const string _playerPrefsSessionId = "OrsSessionId";

    public static UnityEvent OnPasswordValidationAttemptCompleted { get; } = new();

    public static bool MustEnterPassword =>
        !PlayerPrefs.HasKey(_playerPrefsSessionId);

    //private const string _uri = "https://orswebapi-app-20240309191859.ambitioussky-1264637f.eastus.azurecontainerapps.io";
 //  private const string _uri = "https://localhost:7285";
    private const string _uri = "https://ors-d3fjgngqeue0bcd6.canadacentral-01.azurewebsites.net/";

    public static bool Initialized { get; private set; }

    /// <summary>
    /// True if database was not modified 
    /// since the last time everything was cached
    /// </summary>
    public static bool IsUpToDate { get; private set; }

    public static bool AnyCacheErrors { get; private set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="type"></param>
    /// <param name="assetBundleName"></param>
    /// <param name="selectableMetaData"></param>
    /// <returns>If type = <see cref="MetaDataOperationType.Get"/> or 
    /// <see cref="MetaDataOperationType.Set"/>, 
    /// <see cref="MetaDataOperationResult.Message"/> will be 
    /// <see cref="StoredMetaData"/>. If type = 
    /// <see cref="MetaDataOperationType.GetLastModified"/>, 
    /// <see cref="MetaDataOperationResult.Message"/> will 
    /// be a <see cref="long"/>. If message is anything else, 
    /// it's an error.</returns>
    public static async Task<MetaDataOperationResult> DoMetaDataOperation(
    MetaDataOperationType type, 
    string assetBundleName = null, 
    SelectableMetaData selectableMetaData = null)
    {
        var loadingToken = Loading.GetLoadingToken();
        try
        {
            string selectableMetaDataString = selectableMetaData != null ?
                JsonConvert.SerializeObject(selectableMetaData) :
                null;

            var op = new MetaDataOperation()
            {
                OperationType = type,
                AssetBundleName = assetBundleName,
                SelectableMetaData = selectableMetaDataString,
                SessionId = PlayerPrefs.GetString(_playerPrefsSessionId, null),
            };

            var task = DoMetaDataOperation(op);
            await task;

            if (!Application.isPlaying) 
                throw new Exception("App quit during task");

            return task.Result;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);

            return new MetaDataOperationResult
            {
                OperationType = type,
                ResultType = MetaDataOpertaionResultType.Exception,
                Message = $"{ex.GetType().Name} - {ex.Message}"
            };
        }
        finally 
        {
            loadingToken.Done();
        }
    }

    private static async Task<MetaDataOperationResult> DoMetaDataOperation
    (MetaDataOperation operation)
    {
        var loadingToken = Loading.GetLoadingToken();
        try
        {
            var json = JsonConvert.SerializeObject(operation);
            //Debug.Log(json);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string uri = _uri + "/metadata";

            using var request = UnityWebRequest.Put(uri, bytes);
            request.method = "POST";
            request.SetRequestHeader("Content-Type", "application/json");

            var asyncOperation = request.SendWebRequest();
            while (!asyncOperation.isDone)
            {
                await Task.Yield();
                loadingToken.SetProgress(asyncOperation.progress);
                if (!Application.isPlaying)
                    throw new Exception("App quit while downloading data");
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"{request.result} - {request.responseCode}");
                if (request.downloadHandler != null)
                {
                    Debug.LogError(request.downloadHandler.text);
                }
            }

            return JsonConvert.DeserializeObject<MetaDataOperationResult>
                (request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);

            return new MetaDataOperationResult
            {
                OperationType = operation.OperationType,
                ResultType = MetaDataOpertaionResultType.Exception,
                Message = $"{ex.GetType().Name} - {ex.Message}"
            };
        }
        finally
        {
            loadingToken.Done();
        }
    }

    public static async Task<MetaDataOperationResult> SaveMetaData(
    string assetBundleName, 
    SelectableMetaData selectableMetaData)
    {
        var type = MetaDataOperationType.Set;

        var task = DoMetaDataOperation(
            type,
            assetBundleName,
            selectableMetaData);

        await task;

        if (!Application.isPlaying)
            throw new Exception("App quit while in task");

        if (task.Result.ResultType == MetaDataOpertaionResultType.SessionExpired)
        {
            UI_DialogPrompt.Open("Session expired. Re-enter your password to continue.");
            InvalidateSession();
            UI_DbPassword.OpenEnterPassword();
            while (UI_DbPassword.Instance.gameObject.activeSelf)
            {
                await Task.Yield();

                if (!Application.isPlaying)
                    throw new Exception("App quit while in task");
            }

            if (!MustEnterPassword)
            {
                task = SaveMetaData(assetBundleName, selectableMetaData);

                await task;

                if (!Application.isPlaying)
                    throw new Exception("App quit while in task");

                return task.Result;
            }
            else
            {
                UI_DialogPrompt.Open("Save cancelled due to invalid credentials");
                return new MetaDataOperationResult
                {
                    ResultType = MetaDataOpertaionResultType.Error,
                    Message = "Save cancelled due to invalid credentials"
                };
            }
        }
        else if (task.Result.ResultType != MetaDataOpertaionResultType.Success)
        {
            string errorMessage = $"Saving metadata to server failed: {task.Result.Message}";
            Debug.LogError(errorMessage);
            UI_DialogPrompt.Open(errorMessage);
            return new MetaDataOperationResult
            {
                ResultType = MetaDataOpertaionResultType.Error,
                Message = errorMessage
            };
        }

        var message = JsonConvert.DeserializeObject
            <StoredMetaData>(task.Result.Message);

        SaveToCache(assetBundleName, 
            selectableMetaData, message.LastModified);
        
        return task.Result;
    }

    private static void SaveToCache(
    string assetBundleName,
    SelectableMetaData selectableMetaData, 
    long lastModified)
    {
        string path = Path.Combine(
            Application.persistentDataPath,
            _cacheFolderName);

        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string filePath = Path.Combine(path, assetBundleName);

            string serializedJson = JsonConvert
                .SerializeObject(selectableMetaData);

            File.WriteAllText(filePath, serializedJson);

            PlayerPrefs.SetString($"lastModified_{assetBundleName}", lastModified.ToString());

            Debug.Log($"Wrote metadata for asset {assetBundleName} to cache");
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to save to cache");
            Debug.LogException(ex);
            AnyCacheErrors = true;
            IsUpToDate = false;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="assetBundleName"></param>
    /// <param name="seedData">Data that will be saved to the database if the metadata does not exist</param>
    /// <returns><see cref="MetaDataRetrievalResult.MetaData"/> will be null on failure. Check <see cref="MetaDataRetrievalResult.ErrorMessage"/> and <see cref="MetaDataRetrievalResult.ResultType"/> for details</returns>
    public static async Task<MetaDataRetrievalResult> GetMetaData(
    string assetBundleName, SelectableMetaData seedData)
    {
        var loadingToken = Loading.GetLoadingToken();

        try
        {
            string key = $"lastModified_{assetBundleName}";

            if (IsUpToDate)
            {
                // database was not modified since last time we cached everything
                //Debug.Log($"Database was not modified since last cache. Attempting to pull from hard drive cache ...");

                if (TryGetCache(assetBundleName, out var cachedData))
                {
                    return new MetaDataRetrievalResult
                        (MetaDataOpertaionResultType.Success, cachedData);
                }
            }

            var lastModifiedTask = DoMetaDataOperation
                    (MetaDataOperationType.GetLastModified,
                    assetBundleName);

            await lastModifiedTask;

            if (!Application.isPlaying)
                throw new AppQuitInTaskException();

            long lastModified = -1;

            long.TryParse(lastModifiedTask.Result.Message, out lastModified);

            if (PlayerPrefs.HasKey(key) &&
            long.TryParse(PlayerPrefs.GetString(key), out long cachedLastModified))
            {
                if
                // no internet connection or server failure
                (lastModifiedTask.Result.ResultType != MetaDataOpertaionResultType.Success ||

                // cache is up to date
                (!string.IsNullOrEmpty(lastModifiedTask.Result.Message) &&
                lastModified != -1 && cachedLastModified == lastModified) ||

                // doesn't exist on database (should never happen)
                string.IsNullOrEmpty(lastModifiedTask.Result.Message))
                {
                    if (TryGetCache(assetBundleName, out var cachedData))
                    {
                        return new MetaDataRetrievalResult
                            (MetaDataOpertaionResultType.Success, cachedData);
                    }
                }
            }

            var task = DoMetaDataOperation(
                MetaDataOperationType.Get,
                assetBundleName);

            await task;

            if (!Application.isPlaying) 
                throw new AppQuitInTaskException();

            // task.result.message will be of type StoredMetaData
            if (string.IsNullOrEmpty(task.Result.Message) &&
            task.Result.ResultType == MetaDataOpertaionResultType.Success)
            {
                // data entry does not exist, use seed data to make it
                task = SaveMetaData(assetBundleName, seedData);

                await task;

                if (!Application.isPlaying) 
                    throw new AppQuitInTaskException();

                if (task.Result.ResultType == MetaDataOpertaionResultType.Success)
                {
                    return new MetaDataRetrievalResult
                           (MetaDataOpertaionResultType.Success, seedData);
                }
                else
                {
                    string message = $"Encountered an error while trying to save seed data to the server: {task.Result.Message}";

                    Debug.LogError(message);

                    return new MetaDataRetrievalResult
                        (MetaDataOpertaionResultType.Error,
                        null, message);
                }
            }
            else if (task.Result.ResultType != MetaDataOpertaionResultType.Success)
            {
                string message = $"Encountered an error while trying to get data from the server: {task.Result.Message}";
                return new MetaDataRetrievalResult
                    (MetaDataOpertaionResultType.Error,
                    null,
                    message);
            }
            else if (task.Result.ResultType == MetaDataOpertaionResultType.Success && !string.IsNullOrEmpty(task.Result.Message))
            {
                //Debug.Log($"Successfully retrieved metadata from server for asset bundle {assetBundleName}");
                //Debug.Log(task.Result.Message);
                // task.result.message will be of type StoredMetaData
                var storedMetaData = JsonConvert.DeserializeObject<StoredMetaData>(task.Result.Message);
                var selectableMetaData = JsonConvert.DeserializeObject<SelectableMetaData>(storedMetaData.SelectableMetaData);

                if (selectableMetaData == null)
                {
                    string message = "Could not deserialize selectable metadata from database";
                    Debug.LogError(message);
                    return new MetaDataRetrievalResult
                        (MetaDataOpertaionResultType.Error, null, message);
                }
                else if (lastModified == -1)
                {
                    Debug.LogWarning($"Couldn't save to cache - didn't have connection to supply last modified time");
                }
                else
                {
                    //Debug.Log($"Saving {assetBundleName} selectable meta data to cache ...");
                    SaveToCache(assetBundleName, selectableMetaData, lastModified);
                }

                return new MetaDataRetrievalResult
                    (MetaDataOpertaionResultType.Success, selectableMetaData);
            }
            else
            {
                string message = $"Something went wrong while retrieving metadata: {task.Result.Message}";
                Debug.LogError(message);
                return new MetaDataRetrievalResult
                    (MetaDataOpertaionResultType.Error, null, message);
            }
        }
        catch (Exception ex) when (ex is not AppQuitInTaskException)
        { 
            Debug.LogException(ex);
            return new MetaDataRetrievalResult
                (MetaDataOpertaionResultType.Exception, null, $"{ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            loadingToken.Done();
        }
    }

    /// <summary>
    /// Retrieves selectable meta data from a cache in persistent data on the hard drive
    /// </summary>
    /// <param name="assetBundleName"></param>
    /// <param name="selectableMetaData"></param>
    /// <returns></returns>
    private static bool TryGetCache(
    string assetBundleName,
    out SelectableMetaData selectableMetaData)
    {
        Debug.Log($"Retrieving {assetBundleName} from hard drive cache ...");

        selectableMetaData = null;

        string filePath = Path.Combine
            (Application.persistentDataPath, 
            _cacheFolderName, 
            assetBundleName);

        if (!File.Exists(filePath)) return false;

        try
        {
            string file = File.ReadAllText(filePath);

            selectableMetaData = 
                (SelectableMetaData)JsonConvert
                .DeserializeObject(
                    file, 
                    typeof(SelectableMetaData));

            Debug.Log($"Cache {assetBundleName} is valid ? {selectableMetaData != null}");
        }
        catch (Exception ex) 
        { 
            Debug.LogException(ex);
            AnyCacheErrors = true;
        }

        return selectableMetaData != null;
    }

    public static async void SetIsUpToDate()
    {
        var task = GetServerLastModified();
        await task;

        if (!Application.isPlaying)
            throw new Exception("App quit while downloading");

        if (task.Result != -1)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(task.Result);
            Debug.Log($"Setting database cache up to date: {dateTimeOffset.ToLocalTime()}");

            PlayerPrefs.SetString("DatabaseLastModified", task.Result.ToString());
            IsUpToDate = true;
        }
    }

    public static async Task<long> GetServerLastModified()
    {
        var loadingToken = Loading.GetLoadingToken();
        string uri = _uri + "/lastmodified";
        using var request = UnityWebRequest.Get(uri);
        var operation = request.SendWebRequest();

        while (!request.isDone)
        {
            loadingToken.SetProgress(operation.progress);

            await Task.Yield();
            if (!Application.isPlaying)
                throw new Exception("App quit while downloading");
        }

        loadingToken.Done();

        if (request.result != UnityWebRequest.Result.Success)
        {
            // no internet or server malfunction

            Debug.LogError($"Couldn't retrieve database info (no internet?) | response code {request.responseCode}");

            return -1;
        }

        if (!long.TryParse(request.downloadHandler.text, out long serverLastModified))
        {
            // server sent malformed response??

            Debug.LogError("Couldn't parse last modified date/time from server");
            return -1;
        }

        return serverLastModified;
    }

    [RuntimeInitializeOnLoadMethod]
    private static async void OnAppStart()
    {
        if (!PlayerPrefs.HasKey("DatabaseLastModified"))
        {
            Initialized = true;
            return;
        }

        if (!long.TryParse(PlayerPrefs.GetString("DatabaseLastModified"), out long lastModified))
        {
            // something screwed up the playerprefs

            Debug.LogError($"Cached last modified number was corrupted.");

            Initialized = true;
            return;
        }

        var task = GetServerLastModified();
        await task;

        if (!Application.isPlaying)
            throw new Exception("App quit while downloading");

        if (lastModified == task.Result)
        {
            IsUpToDate = true;
        }

        Initialized = true;
    }

    /// <summary>
    /// Is called when a password has been 
    /// entered manually and we are 
    /// sending it to the server to be checked.
    /// Creates and sends a sessionId (guid) 
    /// that is cached here and on the server.
    /// </summary>
    /// <param name="password"></param>
    /// <returns>True if password matched the password in the database</returns>
    /// <exception cref="Exception"></exception>
    public static async Task<bool> ValidatePassword(string password)
    {
        string sessionId = Guid.NewGuid().ToString();
        var task = DoMetaDataOperation(new MetaDataOperation
        {
            Password = password,
            SessionId = sessionId,
            OperationType = MetaDataOperationType.VerifyPassword
        });

        await task;
        if (!Application.isPlaying) 
            throw new AppQuitInTaskException();

        if (task.Result.ResultType == MetaDataOpertaionResultType.PasswordInvalid)
        {
            UI_DialogPrompt.Open("Your password is incorrect. Try again.");
            OnPasswordValidationAttemptCompleted?.Invoke();
            return false;
        }

        if (task.Result.ResultType == MetaDataOpertaionResultType.Success)
        {
            UI_DialogPrompt.Open("Logged in successfully");

            PlayerPrefs.SetString(_playerPrefsSessionId, sessionId);

            OnPasswordValidationAttemptCompleted?.Invoke();
            return true;
        }

        UI_DialogPrompt.Open($"Something went wrong: {task.Result.Message}");
        OnPasswordValidationAttemptCompleted?.Invoke();
        return false;
    }

    public static void LogOut()
    {
        InvalidateSession();
        UI_DialogPrompt.Open("Logged out successfully");
    }

    private static void InvalidateSession()
    {
        PlayerPrefs.DeleteKey(_playerPrefsSessionId);
        OnPasswordValidationAttemptCompleted?.Invoke();
    }

    /// <summary>
    /// Validates cached password
    /// </summary>
    /// <returns>True if password is sessionId and has not expired</returns>
    /// <exception cref="Exception"></exception>
    public static async Task<bool> ValidateSession()
    {
        if (MustEnterPassword) 
        {
            OnPasswordValidationAttemptCompleted?.Invoke();
            return false;
        } 

        var task = DoMetaDataOperation(new MetaDataOperation
        {
            SessionId = PlayerPrefs.GetString(_playerPrefsSessionId, null),
            OperationType = MetaDataOperationType.ValidateSession
        });

        await task;
        if (!Application.isPlaying)
            throw new AppQuitInTaskException();

        if (task.Result.ResultType == MetaDataOpertaionResultType.SessionExpired)
        {
            UI_DialogPrompt.Open("Your session is no longer valid. Please log in again.");
            InvalidateSession();
            OnPasswordValidationAttemptCompleted?.Invoke();
            return false;
        }

        if (task.Result.ResultType == MetaDataOpertaionResultType.Success)
        {
            OnPasswordValidationAttemptCompleted?.Invoke();
            return true;
        }

        UI_DialogPrompt.Open($"Something went wrong: {task.Result.Message}");
        OnPasswordValidationAttemptCompleted?.Invoke();
        return false;
    }

    public static async Task<bool> ChangePassword(string newPassword, string oldPassword)
    {
        var task = DoMetaDataOperation(new MetaDataOperation
        {
            Password = newPassword,
            OperationType = MetaDataOperationType.ChangePassword,
            OldPassword = oldPassword
        });

        await task;
        if (!Application.isPlaying) 
            throw new Exception("App quit during task");

        if (task.Result.ResultType == MetaDataOpertaionResultType.PasswordInvalid)
        {
            UI_DialogPrompt.Open("The current password does not match. Please re-enter the current password");
            return false;
        }

        if (task.Result.ResultType != MetaDataOpertaionResultType.Success)
        {
            UI_DialogPrompt.Open($"Something went wrong: {task.Result.Message}");
            return false;
        }

        UI_DialogPrompt.Open($"Password changed successfully.");

        return true;
    }

    [Serializable]
    public class MetaDataOperation
    {
        public MetaDataOperationType OperationType { get; set; }
        public string AssetBundleName { get; set; }
        public string SelectableMetaData { get; set; }
        public string Password { get; set; }
        public string OldPassword { get; set; }
        public string SessionId { get; set; }
    }

    public class MetaDataRetrievalResult
    {
        public MetaDataRetrievalResult(MetaDataOpertaionResultType resultType, SelectableMetaData metaData)
        {
            ResultType = resultType;
            MetaData = metaData;
        }

        public MetaDataRetrievalResult() { }

        public MetaDataRetrievalResult(MetaDataOpertaionResultType resultType, SelectableMetaData metaData, string errorMessage) : this(resultType, metaData)
        {
            ErrorMessage = errorMessage;
        }

        public MetaDataOpertaionResultType ResultType { get; set; }
        public SelectableMetaData MetaData { get; set; }
        public string ErrorMessage { get; set; }
    }

    [Serializable]
    public class StoredMetaData
    {
        public int ID { get; set; }
        public long LastModified { get; set; }
        public string AssetBundleName { get; set; }
        public string SelectableMetaData { get; set; }
    }

    public enum MetaDataOpertaionResultType
    {
        None,
        Success,
        Error,
        Exception,
        PasswordInvalid,
        SessionExpired
    }

    public enum MetaDataOperationType
    {
        Get,
        Set,
        GetLastModified,
        VerifyPassword,
        ChangePassword,
        ValidateSession
    }

    [Serializable]
    public class MetaDataOperationResult
    {
        public MetaDataOperationType OperationType { get; set; }
        public MetaDataOpertaionResultType ResultType { get; set; }
        public string Message { get; set; }
    }
}