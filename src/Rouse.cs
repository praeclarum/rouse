using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Rouse
{
	public class QueryTypeInfo
	{
		public readonly string Name;
		public readonly PropertyInfo[] Parameters;
		
		public QueryTypeInfo (Type type)
		{
			Name = type.Name;
			Parameters = type
				.GetProperties (BindingFlags.Public | BindingFlags.Instance)
				.Where (x => x.CanWrite)
				.ToArray ();
		}
	}
	
	public abstract class Query
	{
		static object _queryTypeInfoLock = new object ();
		static readonly Dictionary<Type, QueryTypeInfo> _queryTypeInfo = new Dictionary<Type, QueryTypeInfo>();
		
		public virtual QueryTypeInfo TypeInfo
		{
			get {
				QueryTypeInfo info;
				var type = GetType ();
				lock (_queryTypeInfoLock) {
					if (!_queryTypeInfo.TryGetValue (type, out info)) {
						info = new QueryTypeInfo (type);
						_queryTypeInfo.Add (type, info);
					}
				}
				return info;
			}
		}
		
		public virtual string Path
		{
			get {
				return "/" + TypeInfo.Name;
			}
		}
		
		public virtual string Url
		{
			get {
				var sb = new StringBuilder ();
				sb.Append (Path);				
				var info = TypeInfo;
				var head = "?";
				foreach (var p in info.Parameters) {
					sb.Append (head);
					sb.Append (p.Name);
					sb.Append ('=');
					sb.Append (Uri.EscapeDataString (p.GetValue (this, null).ToString ()));
					head = "&";
				}
				return sb.ToString ();
			}
		}
		
		public abstract System.Collections.IEnumerable Perform (ICollectionFactory collections);
	}
	
	public interface ICollectionFactory
	{
		IRepositoryCollection<T> Get<T> ();
	}
	
	public interface IRepositoryCollection<T> : IOrderedQueryable<T>
	{
	}
	
	public class CollectionQuery<T>
	{
		public CollectionQuery<T> Where (Expression<Func<T, bool>> whereExpression)
		{
			throw new NotImplementedException ();
		}
		
		public CollectionQuery<T> OrderByDescending<U> (Expression<Func<T, U>> whereExpression)
		{
			throw new NotImplementedException ();
		}
		
		public CollectionQuery<T> Take (int max)
		{
			throw new NotImplementedException ();
		}
	}
	
	public class QueryResult
	{
		List<object> _items;
		
		public int Count { get { return _items.Count; } }
		public object this [int index] { get { return _items [index]; } }
		
		public QueryResult (System.Collections.IEnumerable enumerable)
		{
			_items = new List<object> (enumerable.Cast<object> ());
		}
		
		public static QueryResult FromXml (System.IO.TextReader textReader)
		{
			using (var xmlReader = new System.Xml.XmlTextReader (textReader)) {
				return FromXml (xmlReader);
			}
		}
		
		public static QueryResult FromXml (System.Xml.XmlReader reader)
		{
			throw new NotImplementedException ();
		}
		
		public void WriteXml (System.IO.TextWriter writer)
		{
			throw new NotImplementedException ();
		}
	}
	
	public class QueryResult<T>
	{
		QueryResult _result;
		
		public QueryResult (QueryResult result)
		{
			_result = result;
		}
		
		public T this [int index] {
			get {
				return (T)_result [index];
			}
		}
	}
		
	public class CacheRepository : Repository
	{
		class CacheItem
		{
			public QueryResult Result;			
			public bool NeedsRefresh { get { return true; } }
		}
		
		readonly Repository _sourceRepository;
		
		readonly object _cacheLock = new object ();
		readonly Dictionary<string, CacheItem> _cache = new Dictionary<string, CacheItem> ();
		
		public CacheRepository (Repository sourceRepository)
		{
			if (sourceRepository == null) throw new ArgumentNullException ("sourceRepository");
			_sourceRepository = sourceRepository;
		}
		
		public override Task<QueryResult> Query (Query query)
		{
			var url = query.Url;
			
			return Task.Factory.StartNew (delegate {
				
				var item = default (CacheItem);
				
				lock (_cacheLock) {
					_cache.TryGetValue (url, out item);
				}
				
				if (item != null) {
					return item.Result;
				}
				else {
					var sourceTask = _sourceRepository.Query (query)
						.ContinueWith ((task) => {
							if (task.Exception != null) throw task.Exception.InnerException;
							var r = task.Result;
							lock (_cacheLock) {
								_cache [url] = new CacheItem {
									Result = r,
								};
							}
							return r;
						});
					return sourceTask.Result;
				}
			});
		}
	}
	
	public class HttpRepository : Repository
	{
		string _hostname;
		CookieContainer _cookies;	
		
		public HttpRepository ()
		{
			_hostname = "";
			_cookies = new CookieContainer ();
		}
		
		public HttpRepository (string hostname)
		{
			_hostname = hostname;
		}
		
		public override Task<QueryResult> Query (Query query)
		{
			var url = query.Url;
			
			if (!url.StartsWith ("http://") && !url.StartsWith ("https://")) {
				if (!url.StartsWith ("/")) {
					url = "/" + url;
				}
				url = "http://" + _hostname + url;
			}
			
			var req = (HttpWebRequest)WebRequest.Create (url);
			req.CookieContainer = _cookies;
			
			return Task.Factory
				.FromAsync<WebResponse> (req.BeginGetResponse, req.EndGetResponse, null)
				.ContinueWith ((resTask) => {
					if (resTask.Exception != null) throw resTask.Exception.InnerException;
					var res = (HttpWebResponse)resTask.Result;
					using (var s = res.GetResponseStream ()) {
						using (var r = new System.IO.StreamReader (s)) {
							return QueryResult.FromXml (r);
						}
					}
				});
		}
	}
	
	public class RouseException : Exception
	{
		public RouseException (string message)
			: base (message)
		{
		}
	}
	
	public abstract class Repository
	{
		public abstract Task<QueryResult> Query (Query query);
		
		public Task<QueryResult<T>> Query<T> (Query query)
		{
			return Query ((Query)query)
				.ContinueWith ((task) => {
					if (task.Exception != null) throw task.Exception.InnerException;
					var gr = task.Result as QueryResult<T>;
					if (gr == null) {
						throw new RouseException ("Query resulted in a list of objects of the wrong type.");
					}
					return gr;
				});
		}
	}
}

