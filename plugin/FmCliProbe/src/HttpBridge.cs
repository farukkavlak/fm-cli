using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading;

namespace FmCliProbe;

/// <summary>
/// Localhost HTTP front door for the CLI.
///
/// Requests land on listener threads, but every il2cpp call has to happen on Unity's main
/// thread, so each request is parked in a queue and woken again once <see cref="Pump"/> has
/// run it from BepInEx's main-thread tick.
/// </summary>
internal sealed class HttpBridge
{
	private int _port;

	/// <summary>The port the bridge took, once started.</summary>
	public int Port => _port;
	private readonly Func<string, NameValueCollection, string> _handle;
	private readonly ConcurrentQueue<Job> _jobs = new();

	private HttpListener _listener;
	private Thread _thread;
	private volatile bool _running;

	public HttpBridge(int port, Func<string, NameValueCollection, string> handle)
	{
		_port = port;
		_handle = handle;
	}

	private sealed class Job
	{
		public string Route;
		public NameValueCollection Query;
		public string Result;
		public readonly ManualResetEventSlim Done = new(false);
	}

	public void Start()
	{
		// A game killed while it held the port can sit in the kernel for a long time, exiting but
		// not gone, and keep the port the whole while (11 Ekim 2026); the next free one is taken
		// then, and written where the CLI looks for it.
		var first = _port;
		for (var port = first; ; port++)
		{
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			try
			{
				_listener.Start();
				_port = port;
				break;
			}
			catch (HttpListenerException) when (port < first + 10)
			{
				try { _listener.Close(); } catch { }
			}
		}
		try
		{
			var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli");
			System.IO.Directory.CreateDirectory(dir);
			System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "port"), _port.ToString());
		}
		catch { /* the CLI falls back to the usual port */ }
		_running = true;

		_thread = new Thread(Listen) { IsBackground = true, Name = "fm-cli-http" };
		_thread.Start();

		Plugin.Log.LogInfo($"[http] listening on http://127.0.0.1:{_port}/");
	}

	public void Stop()
	{
		_running = false;
		try { _listener?.Stop(); } catch { /* shutting down anyway */ }
	}

	private void Listen()
	{
		while (_running)
		{
			HttpListenerContext ctx;
			try { ctx = _listener.GetContext(); }
			catch { return; } // listener stopped

			// Served inline: the CLI issues one command at a time, and every request has to
			// queue for the same main thread anyway, so a worker pool would buy nothing.
			Serve(ctx);
		}
	}

	private void Serve(HttpListenerContext ctx)
	{
		string body;
		var status = 200;
		try
		{
			var job = new Job
			{
				Route = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant(),
				Query = ctx.Request.QueryString,
			};
			_jobs.Enqueue(job);

			// The game ticks at frame rate; anything slower than this is a hang worth reporting.
			body = job.Done.Wait(TimeSpan.FromSeconds(30))
				? job.Result
				: "timeout: the main thread did not run this request within 30s";
			if (body == null) { status = 404; body = "unknown route"; }
		}
		catch (Exception ex)
		{
			status = 500;
			body = ex.ToString();
		}

		try
		{
			var bytes = Encoding.UTF8.GetBytes(body);
			ctx.Response.StatusCode = status;
			ctx.Response.ContentType = "text/plain; charset=utf-8";
			ctx.Response.ContentLength64 = bytes.Length;
			ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
			ctx.Response.Close();
		}
		catch { /* client hung up */ }
	}

	/// <summary>Run queued requests. Must be called from Unity's main thread.</summary>
	public void Pump()
	{
		while (_jobs.TryDequeue(out var job))
		{
			try { job.Result = _handle(job.Route, job.Query); }
			catch (Exception ex) { job.Result = "error: " + ex; }
			finally { job.Done.Set(); }
		}
	}
}
