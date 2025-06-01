using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Networking;

/// <summary>
/// HTTP resource loading service (asynchronous loading).
/// </summary>
public class HttpService : TTMonoBehaviour
{
    static HttpService sInstance = null;
    public const int DEFAULT_TIMEOUT = 30;

    void Awake()
    {
        sInstance = this;
    }

    public static HttpService Instance
    {
        get
        {
            if (sInstance == null)
            {
                GameObject obj = new GameObject();
                obj.name = "HttpServiceObject";
                obj.SetActive(true);
                sInstance = obj.AddComponent<HttpService>();
                GameObject.DontDestroyOnLoad(obj);
            }
            return sInstance;
        }
    }

    // Callback delegates
    public Action<UnityWebRequest, object> LoadFinishDelegate = null;
    public Action<UnityWebRequest> LoadProgressDelegate = null;

    public class URLRequest
    {
        public const int DEFAULT_TIMEOUT = 30;
        public string Url;
        public Action<UnityWebRequest, object> callbacks;
        public Action<UnityWebRequest> progressCallbacks;
        public UnityWebRequest webRequest = null;
        public WWWForm form = null;
        public List<object> callbackParams = new List<object>();
        public float startTime = 0f;
        public int Timeout = 0;

        public URLRequest(string url, Action<UnityWebRequest, object> callback, Action<UnityWebRequest> progressCallback, object param = null, WWWForm form = null, byte[] postData = null, int timeout = 0)
        {
            Url = url;
            callbacks = callback;
            progressCallbacks = progressCallback;
            startTime = Time.realtimeSinceStartup;
            Timeout = timeout <= 0 ? DEFAULT_TIMEOUT : timeout;

            if (postData != null)
            {
                webRequest = UnityWebRequest.Post(url, form);
                webRequest.uploadHandler = new UploadHandlerRaw(postData);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
            }
            else if (form != null)
            {
                webRequest = UnityWebRequest.Post(url, form);
            }
            else
            {
                webRequest = UnityWebRequest.Get(url);
            }

            callbackParams.Add(param);
        }
    }

    private Dictionary<string, URLRequest> DownloadList = new Dictionary<string, URLRequest>();

    public void Load(string url, Action<UnityWebRequest, object> callback, object param, int timeout = 0)
    {
        if (String.IsNullOrEmpty(url))
        {
            KTDebug.LogError("[Http Service] URL is NULL");
            return;
        }
        if (callback == null)
            callback = (UnityWebRequest request, object arg) => { };
        if (timeout == 0)
            timeout = DEFAULT_TIMEOUT;

        StartCoroutine(loadFromRemote(url, callback, (UnityWebRequest req) => { }, param, null, null, timeout));
    }

    public void Load(string url, Action<UnityWebRequest, object> callback, byte[] postData, object param, int timeout = 0)
    {
        if (String.IsNullOrEmpty(url))
        {
            KTDebug.LogError("[Http Service] URL is NULL");
            return;
        }
        if (callback == null)
            callback = (UnityWebRequest request, object arg) => { };
        if (timeout == 0)
            timeout = DEFAULT_TIMEOUT;

        StartCoroutine(loadFromRemote(url, callback, (UnityWebRequest req) => { }, param, null, postData, timeout));
    }

    IEnumerator loadFromRemote(string url, Action<UnityWebRequest, object> callback, Action<UnityWebRequest> progressCallback, object param = null, WWWForm form = null, byte[] postData = null, int timeout = 0)
    {
        UnityWebRequest request = new UnityWebRequest(url)
        {
            timeout = timeout
        };

        if (DownloadList.ContainsKey(url))
        {
            // Thêm callback vào danh sách chờ
            DownloadList[url].callbacks += callback;
            DownloadList[url].progressCallbacks += progressCallback;
            DownloadList[url].callbackParams.Add(param);
        }
        else
        {
            DownloadList.Add(url, new URLRequest(url, callback, progressCallback, param, form, postData, timeout));
        }

        // Gửi yêu cầu và đợi phản hồi
        yield return request.SendWebRequest();

        // Kiểm tra lỗi kết nối và lỗi HTTP
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"Request failed: {request.error}");
        }
        else
        {
            // Khi tải thành công, gọi callback với dữ liệu trả về
            callback?.Invoke(request, param);
        }

        // Cập nhật tiến độ nếu có
        if (progressCallback != null)
        {
            progressCallback.Invoke(request);
        }
    }


    int oldTime = 0;
    void Update()
    {
        List<string> completedItems = null;

        foreach (KeyValuePair<string, URLRequest> requestPair in DownloadList)
        {
            URLRequest request = requestPair.Value;
            UnityWebRequest webRequest = request.webRequest;

            if (webRequest.isDone)
            {
                // Request completed, call the callback
                Action<UnityWebRequest, object> callbacks = request.callbacks;
                while (callbacks != null && callbacks.GetInvocationList().Length > 0)
                {
                    Action<UnityWebRequest, object> callback = (Action<UnityWebRequest, object>)callbacks.GetInvocationList()[0];
                    try
                    {
                        callbacks -= callback;
                        request.progressCallbacks -= (Action<UnityWebRequest>)request.progressCallbacks.GetInvocationList()[0];
                        callback.Invoke(webRequest, request.callbackParams[0]);
                    }
                    catch (Exception e)
                    {
                        KTDebug.LogException(e);
                    }

                    if (request.callbackParams.Count > 0)
                    {
                        request.callbackParams.RemoveAt(0);
                    }
                }

                if (completedItems == null)
                {
                    completedItems = new List<string>();
                }
                completedItems.Add(requestPair.Key);
            }
            else
            {
                // Request still in progress, handle progress update
                if (request.progressCallbacks != null)
                {
                    try
                    {
                        // Timeout check
                        if (Time.realtimeSinceStartup - request.startTime > request.Timeout)
                        {
                            KTDebug.LogWarning("[Http Service] Timeout for " + request.Url);

                            // Handle timeout
                            Action<UnityWebRequest, object> callbacks = request.callbacks;
                            while (callbacks != null && callbacks.GetInvocationList().Length > 0)
                            {
                                Action<UnityWebRequest, object> callback = (Action<UnityWebRequest, object>)callbacks.GetInvocationList()[0];
                                try
                                {
                                    callbacks -= callback;
                                    request.progressCallbacks -= (Action<UnityWebRequest>)request.progressCallbacks.GetInvocationList()[0];
                                    callback.Invoke(null, request.Url);
                                }
                                catch (Exception e)
                                {
                                    KTDebug.LogException(e);
                                }

                                if (request.callbackParams.Count > 0)
                                {
                                    request.callbackParams.RemoveAt(0);
                                }
                            }

                            if (completedItems == null)
                            {
                                completedItems = new List<string>();
                            }
                            completedItems.Add(requestPair.Key);
                        }
                        else
                        {
                            // Update progress
                            ((Action<UnityWebRequest>)request.progressCallbacks.GetInvocationList()[0]).Invoke(webRequest);
                        }
                    }
                    catch (Exception e)
                    {
                        KTDebug.LogException(e);
                    }
                }
            }
        }

        if (completedItems != null)
        {
            foreach (string key in completedItems)
            {
                DownloadList.Remove(key);
            }
            completedItems.Clear();
        }
    }
}
