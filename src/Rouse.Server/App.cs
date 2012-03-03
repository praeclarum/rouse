using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using Rouse.Data;

namespace Rouse.Server
{
	public abstract class ServerResource
	{
		public abstract void Respond (HttpListenerContext context);
	}
	
	public class ResourceListResource : ServerResource
	{
		Query _prototype;
		Repository _repository;
		ServerOptions _options;
		
		public ResourceListResource (Query prototype, Repository repository, ServerOptions options)
		{
			_prototype = prototype;
			_repository = repository;
			_options = options;
		}
		
		public override void Respond (HttpListenerContext context)
		{
			var list = (Query)Activator.CreateInstance (_prototype.GetType ());
			
			_repository
				.Query (list)
				.ContinueWith ((task) => {
					if (task.Exception != null) {
						context.Response.StatusCode = 500;
						if (_options.Debug) {
							using (var writer = new System.IO.StreamWriter (context.Response.OutputStream, System.Text.Encoding.UTF8)) {
								writer.WriteLine (task.Exception.ToString ());
							}
						}
						context.Response.Close ();
					}
					else {
						var result = task.Result;
						context.Response.StatusCode = 200;
						using (var writer = new System.IO.StreamWriter (context.Response.OutputStream, System.Text.Encoding.UTF8)) {
							result.WriteXml (writer);
						}
						context.Response.Close ();
					}
				});
		}
	}
	
	public class ServerOptions
	{
		public string PathToProjectFile { get; set; }
		public string Hostname { get; set; }
		public int Port { get; set; }
		public bool Debug { get; set; }
	}
	
	public class App
	{
		ServerOptions _options;
		
		HttpListener _listener;
		
		Dictionary<string, ServerResource> _resources;
		
		Repository _repository;
		
		public static void Main (string[] args)
		{
			var options = new ServerOptions {
				PathToProjectFile = "",
				Hostname = "*",
				Port = 1337,
				Debug = true,
			};
			
			foreach (var a in args) {
				options.PathToProjectFile = a;
			}
			
			new App (options).Run ();
		}
		
		public App (ServerOptions options)
		{
			_options = options;
		}
		
		void GetResources ()
		{
			_resources = new Dictionary<string, ServerResource>();
			
			var asm = typeof (App).Assembly;
			var queryType = typeof (Query);
			foreach (var t in asm.GetTypes ()) {
				if (queryType.IsAssignableFrom (t) && !t.IsAbstract) {
					
					var q = (Query)Activator.CreateInstance (t);
					var r = new ResourceListResource (q, _repository, _options);
					
					_resources[q.Path] = r;
					
					Console.WriteLine ("{0} -> {1}", q.Path, r);
				}
			}
		}
		
		public void Run ()
		{
			if (string.IsNullOrEmpty (_options.PathToProjectFile)) {
				
				_repository = new CacheRepository (new DataRepository (new Mono.Data.Sqlite.SqliteConnection ()));
				
				GetResources ();
				
				RunHttpServer ();
			}
		}
		
		void RunHttpServer ()
		{
			var prefix = "http://" + _options.Hostname + ":" + _options.Port + "/";
			
			_listener = new HttpListener ();
			_listener.Prefixes.Add (prefix);
			_listener.Start ();
			_listener.BeginGetContext (OnGetContext, null);
			
			Console.WriteLine ("Listing at " + prefix);
			
			for (;;) {
				System.Threading.Thread.Sleep (5000);
			}
		}
		
		void OnGetContext (IAsyncResult ar)
		{
			var context = default(HttpListenerContext);
			try {
				context = _listener.EndGetContext (ar);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}
			
			_listener.BeginGetContext (OnGetContext, null);
			
			if (context != null) {
				
				var path = context.Request.Url.AbsolutePath;
				
				ServerResource resource;
				
				if (_resources.TryGetValue (path, out resource)) {
					resource.Respond (context);
				}
				else {
					context.Response.StatusCode = 404;
					context.Response.Close ();
				}
			}
		}
	}
}
