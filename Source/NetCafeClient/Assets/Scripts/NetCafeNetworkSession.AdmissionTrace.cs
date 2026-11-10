#if NETCAFE_B1
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

public sealed partial class NetCafeNetworkSession
{
    public enum AdmissionStep { None, Health, Readiness, Reservation, LiveKit, Confirm }

    // The report is built only from fixed labels, timings and allowlisted codes.
    // No URL, player input, credentials, exception text or response bodies.
    sealed class AdmissionTrace
    {
        readonly int[] attempts = new int[6];
        readonly double[] elapsed = new double[6];
        readonly string[] outcomes = new string[6];
        readonly DateTime utc = DateTime.UtcNow;
        readonly double started = Time.realtimeSinceStartupAsDouble;
        readonly bool join;
        double stepStarted, total;
        bool active;
        string failure;
        public AdmissionStep Step { get; private set; }

        public AdmissionTrace(bool join) { this.join = join; }

        public void Begin(AdmissionStep next)
        {
            Step = next;
            attempts[(int)next]++;
            stepStarted = Time.realtimeSinceStartupAsDouble;
            active = true;
        }

        public void End(string result)
        {
            if (!active) return;
            elapsed[(int)Step] += Time.realtimeSinceStartupAsDouble - stepStarted;
            outcomes[(int)Step] = SafeCode(result);
            active = false;
        }

        public void Fail(string code)
        {
            End(code);
            failure = SafeCode(code);
            total = Time.realtimeSinceStartupAsDouble - started;
        }

        public string Format()
        {
            if (failure == null) return "";
            var report = new StringBuilder("LibraryLounge R11 WAN Stability\n");
            report.Append("UTC: ").Append(utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).AppendLine();
            report.Append("操作: ").Append(join ? "加入房间" : "创建房间").AppendLine();
            report.Append("失败阶段: ").Append(Label(Step)).Append(" / ").Append(failure).AppendLine();
            for (int i = 1; i < attempts.Length; i++)
            {
                if (attempts[i] == 0) continue;
                report.Append(Label((AdmissionStep)i)).Append(": ").Append(attempts[i]).Append(" 次，")
                    .Append(elapsed[i].ToString("F1", CultureInfo.InvariantCulture)).Append(" 秒，")
                    .Append(outcomes[i]).AppendLine();
            }
            return report.Append("总耗时: ").Append(total.ToString("F1", CultureInfo.InvariantCulture)).Append(" 秒").ToString();
        }

        static string Label(AdmissionStep step) => step switch
        {
            AdmissionStep.Health => "房间服务健康检查",
            AdmissionStep.Readiness => "LiveKit 就绪检查",
            AdmissionStep.Reservation => "开房或加入请求",
            AdmissionStep.LiveKit => "LiveKit 连接",
            AdmissionStep.Confirm => "入房确认",
            _ => "入房前检查"
        };

        static string SafeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "OK";
            if (code.Length == 8 && code.StartsWith("HTTP_", StringComparison.Ordinal) &&
                code[5] >= '1' && code[5] <= '5' && code[6] >= '0' && code[6] <= '9' &&
                code[7] >= '0' && code[7] <= '9') return code;
            return code switch
            {
                "NETWORK_UNREACHABLE" or "SERVICE_WAKE_TIMEOUT" or "INVALID_HEALTH_REPLY" or "INVALID_READY_REPLY" or
                "LIVEKIT_NOT_READY" or "ROOM_API_NO_RESPONSE" or "LIVEKIT_CONNECT_FAILED" or "WINDOWS_LOW_INTEGRITY" or
                "ROOM_CONFIRM_NO_RESPONSE" or "LIVEKIT_DISCONNECTED_DURING_CONFIRM" or "INVALID_SERVER_REPLY" or
                "INVALID_INVITATION" or "ROOM_FULL" or "INVALID_NICKNAME" or "ROOM_CREATION_FAILED" or
                "RATE_LIMITED" or "SERVICE_UNAVAILABLE" or "SESSION_EXPIRED" => code,
                _ => "UNKNOWN_ERROR"
            };
        }
    }
}
#endif
