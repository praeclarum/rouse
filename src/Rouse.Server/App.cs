using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using Rouse.Data;
using System.Text;

namespace Rouse.Server
{
	public abstract class Responder
	{
		protected readonly Repository _repository;
		protected readonly ServerOptions _options;
		
		public Responder (Repository repository, ServerOptions options)
		{
			_repository = repository;
			_options = options;
		}
		
		public abstract void Respond (HttpListenerContext context);
		
		protected void RespondWithError (Exception err, HttpListenerResponse res)
		{
			var encoding = Encoding.UTF8;
			
			res.StatusCode = 500;
			if (_options.Debug) {
				res.ContentType = "text/plain";
				res.ContentEncoding = encoding;
				var mem = new System.IO.MemoryStream ();
				using (var writer = new System.IO.StreamWriter (mem, System.Text.Encoding.UTF8)) {
					writer.WriteLine (err.ToString ());
				}
				var b = mem.ToArray ();
				res.ContentLength64 = b.Length;
				using (var s = res.OutputStream) {
					s.Write (b, 0, b.Length);
				}
			}
			res.Close ();
		}
	}
	
	public class ResourceResponder : Responder
	{
		Resource _prototype;
		
		public ResourceResponder (Resource prototype, Repository repository, ServerOptions options)
			: base (repository, options)
		{
			_prototype = prototype;
		}
		
		public override void Respond (HttpListenerContext context)
		{
			var resource = (Resource)Activator.CreateInstance (_prototype.GetType ());
			var req = context.Request;
			var res = context.Response;
			
			try {				
				if (req.HttpMethod == "POST") {
					Console.WriteLine ("POST");
					using (var reqStream = req.InputStream) {
						resource.ReadXml (reqStream);
					}
				}
				//else if (req.HttpMethod == "GET") {
				//}
				else {
					res.StatusCode = 405;
					res.Close ();
				}
			} catch (Exception ex) {
				RespondWithError (ex, res);
			}
		}
	}
	
	public class QueryResponder : Responder
	{
		Query _prototype;
		
		public QueryResponder (Query prototype, Repository repository, ServerOptions options)
			: base (repository, options)
		{
			_prototype = prototype;
		}
		
		public override void Respond (HttpListenerContext context)
		{
			var list = (Query)Activator.CreateInstance (_prototype.GetType ());
			
			_repository
				.Get (list)
				.ContinueWith ((task) => {
					var encoding = System.Text.Encoding.UTF8;
						
					Exception err = task.Exception;					
					if (err == null) {
						var result = task.Result;
						try {
							var contentType = "application/xml";
							var mem = new System.IO.MemoryStream ();
							result.WriteContent (contentType, mem, encoding);
							context.Response.StatusCode = 200;
							context.Response.ContentType = contentType;
							context.Response.ContentEncoding = encoding;
							var b = mem.ToArray ();
							context.Response.ContentLength64 = b.Length;
							using (var s = context.Response.OutputStream) {
								s.Write (b, 0, b.Length);
							}
							context.Response.Close ();
						}
						catch (Exception ex) {
							err = ex;
						}
					}
					if (err != null) {
						RespondWithError (err, context.Response);
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
		
		Dictionary<string, Responder> _responders;
		
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
		
		void GetResponders ()
		{
			_responders = new Dictionary<string, Responder>();
			
			var asm = typeof (App).Assembly;
			
			var queryType = typeof (Query);
			var resourceType = typeof (Resource);
			
			foreach (var t in asm.GetTypes ()) {
				if (queryType.IsAssignableFrom (t) && !t.IsAbstract) {
					
					var query = (Query)Activator.CreateInstance (t);
					var r = new QueryResponder (query, _repository, _options);
					
					_responders [query.Path] = r;
					
					Console.WriteLine ("{0} -> {1}", query.Path, r);
				}
				else if (resourceType.IsAssignableFrom (t) && !t.IsAbstract) {
					
					var resource = (Resource)Activator.CreateInstance (t);
					var r = new ResourceResponder (resource, _repository, _options);
					
					_responders [resource.Path] = r;
					
					Console.WriteLine ("{0} -> {1}", resource.Path, r);
				}
			}
		}
		
		public void Run ()
		{
			if (string.IsNullOrEmpty (_options.PathToProjectFile)) {
				
				_repository = new CacheRepository (new DataRepository (new Mono.Data.Sqlite.SqliteConnection ("Data Source=file::memory:")));
				
				GetResponders ();
				
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
				
				Responder resource;
				
				if (_responders.TryGetValue (path, out resource)) {
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
