using System.IO;
using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace BigPhone;

/// <summary>
/// Wraps SIPSorcery: registration with FlyNumber, one call at a time, in and out.
/// All events are raised on background threads; the UI must marshal to its dispatcher.
/// </summary>
public sealed class SipPhone : IDisposable
{
    readonly PhoneSettings _s;
    readonly string _password;
    readonly object _lock = new();

    SIPTransport? _transport;
    SIPRegistrationUserAgent? _reg;
    SIPUserAgent? _ua;
    SIPServerUserAgent? _pendingIncoming;
    WindowsAudioEndPoint? _audio;
    VoIPMediaSession? _media;
    Timer? _keepAlive;
    SIPEndPoint? _serverEndPoint;
    IPAddress? _publicIp;
    bool _outgoing;

    public event Action<bool, string>? RegistrationChanged;   // ok, message
    public event Action<string, string>? IncomingCall;        // number, display name
    public event Action? IncomingCancelled;
    public event Action<string>? CallProgress;                // "Ringing..."
    public event Action? CallAnswered;
    public event Action<string>? CallEnded;                   // reason

    public SipPhone(PhoneSettings settings, string password)
    {
        _s = settings;
        _password = password;
    }

    public bool IsRegistered { get; private set; }
    public bool IsInCall => _ua?.IsCallActive == true;
    public bool IsMuted { get; private set; }

    // ------------------------------------------------------------------ start / stop

    public void Start()
    {
        Log.Write("Starting SIP phone");
        _transport = new SIPTransport();
        _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, _s.LocalSipPort)));

        if (_s.SipTrace)
        {
            _transport.SIPRequestOutTraceEvent += (l, r, req) => Log.Write($"SIP OUT to {r}:\n{req}");
            _transport.SIPResponseOutTraceEvent += (l, r, resp) => Log.Write($"SIP OUT to {r}:\n{resp}");
            _transport.SIPRequestInTraceEvent += (l, r, req) => Log.Write($"SIP IN from {r}:\n{req}");
            _transport.SIPResponseInTraceEvent += (l, r, resp) => Log.Write($"SIP IN from {r}:\n{resp}");
        }

        _ua = new SIPUserAgent(_transport, null, true);
        _ua.ClientCallTrying += (uac, resp) => CallProgress?.Invoke("Calling…");
        _ua.ClientCallRinging += (uac, resp) => CallProgress?.Invoke("Ringing…");
        _ua.ClientCallAnswered += (uac, resp) =>
        {
            Log.Write($"Outgoing call answered: {resp.StatusCode}");
        };
        _ua.ClientCallFailed += (uac, err, resp) =>
        {
            Log.Write($"Outgoing call failed: {err} {resp?.StatusCode}");
            EndMedia();
            CallEnded?.Invoke(FriendlyFailure(resp?.Status, err));
        };
        _ua.OnCallHungup += dlg =>
        {
            Log.Write("Call hung up by other side");
            EndMedia();
            CallEnded?.Invoke("Call ended");
        };
        _ua.ServerCallCancelled += (uas, cancelReq) =>
        {
            Log.Write("Incoming call cancelled by caller");
            lock (_lock) _pendingIncoming = null;
            IncomingCancelled?.Invoke();
        };
        _ua.OnIncomingCall += OnIncomingCall;

        string authUser = string.IsNullOrWhiteSpace(_s.AuthId) ? _s.Username : _s.AuthId;
        _reg = new SIPRegistrationUserAgent(_transport, authUser, _password, _s.Server, _s.RegisterExpirySeconds);
        _reg.RegistrationSuccessful += (uri, resp) =>
        {
            IsRegistered = true;
            Log.Write($"Registered as {uri}");
            RegistrationChanged?.Invoke(true, "Ready");
        };
        _reg.RegistrationFailed += (uri, resp, err) =>
        {
            IsRegistered = false;
            Log.Write($"Registration failed: {err}");
            RegistrationChanged?.Invoke(false, resp?.StatusCode is 401 or 403 ? "Wrong password" : "Not connected");
        };
        _reg.RegistrationTemporaryFailure += (uri, resp, err) =>
        {
            IsRegistered = false;
            Log.Write($"Registration temporary failure: {err}");
            RegistrationChanged?.Invoke(false, "Not connected – retrying");
        };
        _reg.Start();

        _ = Task.Run(ResolveNetworkAsync);
    }

    async Task ResolveNetworkAsync()
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(_s.Server);
            var ip = addrs.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            _serverEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(ip, 5060));
            if (_s.KeepAliveSeconds > 0)
            {
                var period = TimeSpan.FromSeconds(_s.KeepAliveSeconds);
                _keepAlive = new Timer(_ => SendKeepAlive(), null, period, period);
            }
        }
        catch (Exception ex) { Log.Write($"DNS lookup of {_s.Server} failed: {ex.Message}"); }

        try
        {
            if (!string.IsNullOrWhiteSpace(_s.StunServer))
            {
                var parts = _s.StunServer.Split(':');
                int port = parts.Length > 1 ? int.Parse(parts[1]) : 3478;
                _publicIp = STUNClient.GetPublicIPAddress(parts[0], port);
                Log.Write($"Public IP from STUN: {_publicIp}");
            }
        }
        catch (Exception ex) { Log.Write($"STUN failed: {ex.Message}"); }
    }

    void SendKeepAlive()
    {
        try
        {
            var channel = _transport?.GetSIPChannels().FirstOrDefault();
            if (channel != null && _serverEndPoint != null)
                _ = _transport!.SendRawAsync(channel.ListeningSIPEndPoint, _serverEndPoint, "\r\n\r\n"u8.ToArray());
        }
        catch (Exception ex) { Log.Write($"Keep-alive failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        _keepAlive?.Dispose();
        try { if (IsInCall) _ua?.Hangup(); } catch { }
        try { _reg?.Stop(); } catch { }
        EndMedia();
        try { _ua?.Dispose(); } catch { }
        try { _transport?.Shutdown(); } catch { }
    }

    // ------------------------------------------------------------------ outgoing

    public async Task CallAsync(string number)
    {
        if (_ua == null) return;
        _outgoing = true;
        string dst = $"sip:{number}@{_s.Domain}";
        Log.Write($"Calling {dst}");
        string authUser = string.IsNullOrWhiteSpace(_s.AuthId) ? _s.Username : _s.AuthId;
        string from = string.IsNullOrWhiteSpace(_s.DisplayName)
            ? $"<sip:{_s.Username}@{_s.Domain}>"
            : $"\"{_s.DisplayName}\" <sip:{_s.Username}@{_s.Domain}>";

        var cd = new SIPCallDescriptor(authUser, _password, dst, from, dst, null, null, null,
            SIPCallDirection.Out, SDP.SDP_MIME_CONTENTTYPE, null, null);

        CreateMedia();
        try
        {
            bool ok = await _ua.Call(cd, _media!, _s.RingTimeoutSeconds);
            if (ok)
            {
                Log.Write("Call connected");
                CallAnswered?.Invoke();
            }
            // failures are reported through ClientCallFailed
        }
        catch (Exception ex)
        {
            Log.Write($"Call error: {ex}");
            EndMedia();
            CallEnded?.Invoke("Call failed");
        }
    }

    // ------------------------------------------------------------------ incoming

    void OnIncomingCall(SIPUserAgent ua, SIPRequest req)
    {
        string number = req.Header.From?.FromURI?.User ?? "Unknown";
        string name = req.Header.From?.FromName ?? "";
        Log.Write($"Incoming call from {number} \"{name}\"");

        lock (_lock)
        {
            if (ua.IsCallActive || ua.IsCalling || _pendingIncoming != null)
            {
                var busy = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BusyHere, null);
                _ = _transport!.SendResponseAsync(busy);
                return;
            }
            _pendingIncoming = ua.AcceptCall(req);
        }
        IncomingCall?.Invoke(number, name);
    }

    public async Task AnswerAsync()
    {
        SIPServerUserAgent? uas;
        lock (_lock) { uas = _pendingIncoming; _pendingIncoming = null; }
        if (uas == null || _ua == null) return;
        _outgoing = false;
        CreateMedia();
        bool ok = await _ua.Answer(uas, _media!);
        if (ok)
        {
            Log.Write("Answered");
            CallAnswered?.Invoke();
        }
        else
        {
            Log.Write("Answer failed");
            EndMedia();
            CallEnded?.Invoke("Could not answer");
        }
    }

    public void Decline()
    {
        SIPServerUserAgent? uas;
        lock (_lock) { uas = _pendingIncoming; _pendingIncoming = null; }
        uas?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        Log.Write("Declined incoming call");
    }

    // ------------------------------------------------------------------ in-call

    public void HangUp()
    {
        if (_ua == null) return;
        if (_ua.IsCallActive) _ua.Hangup();
        else if (_outgoing) _ua.Cancel();
        Log.Write("Hung up");
        EndMedia();
    }

    public async Task SendDtmfAsync(char key)
    {
        if (_ua == null || !_ua.IsCallActive) return;
        byte code = key switch { '*' => 10, '#' => 11, _ => (byte)(key - '0') };
        await _ua.SendDtmf(code);
    }

    public void SetMute(bool mute)
    {
        IsMuted = mute;
        if (_audio == null) return;
        if (mute) _audio.PauseAudio(); else _audio.ResumeAudio();
    }

    // ------------------------------------------------------------------ media

    void CreateMedia()
    {
        EndMedia();
        int outIdx = FindOutputDevice(_s.AudioOutputDevice);
        int inIdx = FindInputDevice(_s.AudioInputDevice);
        Log.Write($"Audio devices: out={outIdx} in={inIdx}");

        _audio = new WindowsAudioEndPoint(new AudioEncoder(), outIdx, inIdx);
        _audio.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU || f.Codec == AudioCodecsEnum.PCMA || f.Codec == AudioCodecsEnum.G722);
        _media = new NatMediaSession(_audio.ToMediaEndPoints(), _publicIp);
        _media.AcceptRtpFromAny = true;
        IsMuted = false;
    }

    void EndMedia()
    {
        try { _media?.Close("bye"); } catch { }
        try { _audio?.CloseAudio(); } catch { }
        _media = null;
        _audio = null;
        IsMuted = false;
    }

    public static int FindOutputDevice(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        for (int i = 0; i < WaveOut.DeviceCount; i++)
            if (NameMatches(WaveOut.GetCapabilities(i).ProductName, name)) return i;
        return -1;
    }

    public static int FindInputDevice(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            if (NameMatches(WaveIn.GetCapabilities(i).ProductName, name)) return i;
        return -1;
    }

    // WinMM truncates names to 31 characters, so compare prefixes both ways.
    static bool NameMatches(string device, string wanted) =>
        device.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) ||
        wanted.StartsWith(device, StringComparison.OrdinalIgnoreCase);

    static string FriendlyFailure(SIPResponseStatusCodesEnum? status, string err) => status switch
    {
        SIPResponseStatusCodesEnum.BusyHere or SIPResponseStatusCodesEnum.BusyEverywhere => "Line busy",
        SIPResponseStatusCodesEnum.TemporarilyUnavailable or SIPResponseStatusCodesEnum.RequestTimeout => "No answer",
        SIPResponseStatusCodesEnum.NotFound or SIPResponseStatusCodesEnum.AddressIncomplete => "Wrong number",
        SIPResponseStatusCodesEnum.Decline => "Call declined",
        SIPResponseStatusCodesEnum.PaymentRequired => "Out of credit",
        SIPResponseStatusCodesEnum.RequestTerminated => "Call cancelled",
        _ => "Call did not connect",
    };
}

/// <summary>Puts the public IP (from STUN) in the SDP so the provider sends audio to the router, not a private address.</summary>
sealed class NatMediaSession : VoIPMediaSession
{
    readonly IPAddress? _publicIp;
    public NatMediaSession(MediaEndPoints media, IPAddress? publicIp) : base(media) => _publicIp = publicIp;
    public override SDP CreateOffer(IPAddress connectionAddress) => base.CreateOffer(connectionAddress ?? _publicIp!);
    public override SDP CreateAnswer(IPAddress connectionAddress) => base.CreateAnswer(connectionAddress ?? _publicIp!);
}

public static class Log
{
    static readonly object Lock = new();

    public static void Write(string msg)
    {
        try
        {
            lock (Lock)
            {
                var fi = new FileInfo(PhoneSettings.LogPath);
                if (fi.Exists && fi.Length > 5_000_000) fi.Delete();
                File.AppendAllText(PhoneSettings.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
