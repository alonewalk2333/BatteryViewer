using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace BatteryViewer
{
    // 本地 HTTP API：只监听 127.0.0.1 回环，供本机其它程序/脚本查询电量。
    // GET /api/battery  -> JSON 设备电量数组
    // GET /api/ping     -> {"ok":true}
    public class ApiServer
    {
        public class Snapshot
        {
            public string Name;
            public string VidPid;
            public bool Ok;
            public int? Level;
            public string Status;
            public int? VoltageMv;
            public string Source;
            public string Detail;
        }

        readonly Func<List<Snapshot>> provider;
        readonly Func<int> thresholdProvider;
        HttpListener listener;
        Thread worker;
        volatile bool running;

        public int Port { get; private set; }
        public string LastError = "";

        public ApiServer(int port, Func<List<Snapshot>> provider, Func<int> thresholdProvider = null)
        {
            Port = port;
            this.provider = provider;
            this.thresholdProvider = thresholdProvider;
        }

        public bool Start()
        {
            Stop();
            try
            {
                listener = new HttpListener();
                // "localhost" 前缀非管理员即可监听；127.0.0.1 需要管理员配 URLACL
                listener.Prefixes.Add("http://localhost:" + Port + "/");
                listener.Start();
                running = true;
                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Start();
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                try { if (listener != null) listener.Close(); } catch { }
                listener = null;
                running = false;
                return false;
            }
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) { listener.Stop(); listener.Close(); } } catch { }
            listener = null;
        }

        // 内嵌电量面板（GET / 或 /widget 直接伺服，同源无跨域问题）
        static readonly string WidgetHtml =
            @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<title>外设电量</title>
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: ""Microsoft YaHei UI"", ""Segoe UI"", sans-serif;
    background: linear-gradient(135deg, #1a1d24 0%, #232733 100%);
    color: #e8ebf2;
    min-height: 100vh;
    display: flex;
    justify-content: center;
    padding-top: 60px;
  }
  .panel {
    width: 340px;
    background: rgba(30, 33, 41, 0.6);
    border: 1px solid rgba(255,255,255,0.08);
    border-radius: 16px;
    padding: 24px;
    height: fit-content;
    backdrop-filter: blur(20px);
  }
  h1 { font-size: 15px; font-weight: 600; margin-bottom: 4px; }
  .sub { font-size: 11px; color: #787e8a; margin-bottom: 18px; }
  .row { margin-bottom: 14px; }
  .name { font-size: 13px; margin-bottom: 5px; display: flex; justify-content: space-between; }
  .pct { font-weight: 700; }
  .track { height: 6px; background: rgba(255,255,255,0.08); border-radius: 3px; overflow: hidden; }
  .fill { height: 100%; border-radius: 3px; transition: width .5s; }
  .hi  { background: #2ecc71; }
  .mid { background: #f1c40f; }
  .lo  { background: #e74c3c; }
  .empty { font-size: 12px; color: #969ca8; text-align: center; padding: 24px 0; line-height: 1.8; }
  .foot { margin-top: 18px; font-size: 10px; color: #5a6473; text-align: center; }
  .err { color: #e74c3c; font-size: 12px; text-align: center; padding: 20px 0; line-height: 1.8; }
</style>
</head>
<body>
<div class=""panel"">
  <h1>外设电量</h1>
  <div class=""sub"" id=""time"">连接中…</div>
  <div id=""list""><div class=""empty"">正在读取…</div></div>
  <div class=""foot"">每 30 秒自动刷新 · 数据来自 BatteryViewer 本地 API</div>
</div>
<script>
function colorOf(lv) {
  if (lv >= 60) return ""hi"";
  if (lv >= 30) return ""mid"";
  return ""lo"";
}
async function load() {
  try {
    const API = location.protocol.startsWith(""http"") ? ""api/battery?readable=1"" : ""http://localhost:8787/api/battery?readable=1"";
    const r = await fetch(API);
    const d = await r.json();
    document.getElementById(""time"").textContent = ""更新于 "" + d.time;
    const el = document.getElementById(""list"");
    if (!d.devices.length) {
      el.innerHTML = '<div class=""empty"">没有读到电量<br>先动一下设备，确认软件里能刷出来</div>';
      return;
    }
    el.innerHTML = d.devices.map(v => `
      <div class=""row"">
        <div class=""name""><span>${v.name.length > 18 ? v.name.slice(0,17) + ""…"" : v.name}</span>
        <span class=""pct"" style=""color:var(--c)"">${v.level}%</span></div>
        <div class=""track""><div class=""fill ${colorOf(v.level)}"" style=""width:${v.level}%;--c:currentColor""></div></div>
      </div>`).join("""");
    // 百分比颜色
    el.querySelectorAll("".pct"").forEach((p, i) => {
      p.style.color = d.devices[i].level >= 60 ? ""#2ecc71"" : d.devices[i].level >= 30 ? ""#f1c40f"" : ""#e74c3c"";
    });
  } catch (e) {
    document.getElementById(""time"").textContent = ""连接失败"";
    document.getElementById(""list"").innerHTML =
      '<div class=""err"">连不上 API（localhost:8787）<br>先打开 BatteryViewer<br>设置 → 本地 API → 勾选开启</div>';
  }
}
load();
setInterval(load, 30000);
</script>
</body>
</html>
";

        void Loop()
        {
            while (running && listener != null)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = listener.GetContext();
                    Handle(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (Exception) { }
                finally { try { if (ctx != null) ctx.Response.Close(); } catch { } }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            // Chrome/Edge 对 file:// 页面访问 localhost 的 PNA（专用网络访问）预检：
            // 必须响应 OPTIONS 且带 Access-Control-Allow-Private-Network，否则浏览器静默拦截
            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Private-Network", "true");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "*");
                return;
            }

            // 自带面板：同源访问，零跨域问题
            if (ctx.Request.Url.AbsolutePath == "/" || ctx.Request.Url.AbsolutePath == "/widget")
            {
                byte[] html = Encoding.UTF8.GetBytes(WidgetHtml);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = html.Length;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                try { ctx.Response.OutputStream.Write(html, 0, html.Length); } catch { }
                return;
            }

            string path = ctx.Request.Url.AbsolutePath;
            bool onlyReadable = (ctx.Request.QueryString["readable"] == "1");
            string json;
            if (path == "/api/battery")
            {
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"devices\":[");
                List<Snapshot> snap = null;
                try { snap = provider(); } catch { }
                if (snap != null)
                {
                    if (onlyReadable) snap = snap.FindAll(d => d.Ok);
                    bool first = true;
                    foreach (var d in snap)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":\"").Append(JsonEsc(d.Name))
                          .Append("\",\"vidpid\":\"").Append(JsonEsc(d.VidPid))
                          .Append("\",\"ok\":").Append(d.Ok ? "true" : "false")
                          .Append(",\"level\":").Append(d.Level.HasValue ? d.Level.Value.ToString() : "null")
                          .Append(",\"status\":\"").Append(JsonEsc(d.Status))
                          .Append("\",\"voltage_mv\":").Append(d.VoltageMv.HasValue ? d.VoltageMv.Value.ToString() : "null")
                          .Append(",\"source\":\"").Append(JsonEsc(d.Source))
                          .Append("\",\"detail\":\"").Append(JsonEsc(d.Detail))
                          .Append("\"}");
                    }
                }
                sb.Append("],\"time\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\"}");
                json = sb.ToString();
            }
            else if (path == "/api/ping")
            {
                json = "{\"ok\":true,\"app\":\"BatteryViewer\"}";
            }
            else if (path == "/api/alerts")
            {
                int th = 20;
                try { if (thresholdProvider != null) th = thresholdProvider(); } catch { }
                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"threshold\":").Append(th).Append(",\"alerts\":[");
                List<Snapshot> snap = null;
                try { snap = provider(); } catch { }
                bool first = true;
                if (snap != null)
                    foreach (var d in snap)
                    {
                        if (!d.Ok || !d.Level.HasValue || d.Level.Value > th) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":\"").Append(JsonEsc(d.Name))
                          .Append("\",\"level\":").Append(d.Level.Value).Append("}");
                    }
                sb.Append("]}");
                json = sb.ToString();
            }
            else
            {
                json = "{\"ok\":false,\"error\":\"unknown endpoint\",\"endpoints\":[\"/api/battery\",\"/api/ping\",\"/api/alerts\",\"/widget\"]}";
                ctx.Response.StatusCode = 404;
            }

            byte[] buf = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            // 允许本机网页直接 fetch（含 file:// 页面，PNA 需要额外声明）
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.AddHeader("Access-Control-Allow-Private-Network", "true");
            try { ctx.Response.OutputStream.Write(buf, 0, buf.Length); } catch { }
        }

        static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
