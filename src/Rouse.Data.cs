using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Rouse.Data
{
	public class DataRepository : Repository
	{
		IDbConnection _connection;
		readonly object _connectionLock = new object ();
		
		Collections _collections;
		
		public DataRepository (IDbConnection connection)
		{
			if (connection == null) throw new ArgumentNullException ("connection");
			_connection = connection;
			
			_collections = new Collections (this);
		}
		
		class Collections : ICollectionFactory
		{
			DataRepository _repo;
			
			public Collections (DataRepository repo)
			{
				_repo = repo;
			}
			
			public IRepositoryCollection<T> Get<T> ()
			{
				return new Collection<T> (_repo);
			}
		}
		
		class OrderBy
		{
			public string Text;
			public string Direction;
		}
		
		class Parameter
		{
			public string Name;
			public object Value;
		}
		
		class Collection<T> : IRepositoryCollection<T>, IOrderedQueryable<T>
		{
			DataRepository _repo;
			
			public Type ElementType { get { return typeof(T); } }
			
			public Collection (Expression expression, DataRepository repo)
			{
				_repo = repo;
				Expression = expression;
				Provider = new CollectionQueryProvider<T> (this);
			}
			
			public Collection (DataRepository repo)
			{
				_repo = repo;
				Expression = Expression.Constant (this);
				Provider = new CollectionQueryProvider<T> (this);
			}
			
			public Expression Expression { get; private set; }
			
			public IQueryProvider Provider { get; private set; }
			
			public IEnumerator<T> GetEnumerator ()
			{
				List<T> r = new List<T>();
				
				using (var cmd = _repo._connection.CreateCommand ()) {
					
					cmd.CommandText = SqlCommandText;
					
					foreach (var p in _params) {
						var dbp = cmd.CreateParameter ();
						dbp.ParameterName = p.Name;
						dbp.Value = p.Value;
						cmd.Parameters.Add (dbp);
					}
					
					if (_repo._connection.State != ConnectionState.Open) {
						_repo._connection.Open ();
					}
					
					using (var reader = cmd.ExecuteReader ()) {
						while (reader.Read ()) {
							Console.WriteLine (reader);
						}
					}
				}
				
				return r.GetEnumerator ();
			}
			
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
			{
				return ((IEnumerable<T>)this).GetEnumerator ();
			}
			
			public string SqlCommandText
			{
				get {
					var sb = new StringBuilder ();
					
					sb.AppendFormat ("select {0} from \"{1}\"", _select, typeof(T).Name);
					
					var head = " where ";
					foreach (var w in _wheres) {
						sb.Append (head);
						sb.Append (w);
						head = " and ";
					}
					
					head = " order by ";
					foreach (var o in _orderbys) {
						sb.Append (head);
						sb.Append (o.Text);
						sb.Append (' ');
						sb.Append (o.Direction);
						head = ",";
					}
					
					if (!string.IsNullOrEmpty (_take)) {
						sb.Append (" limit ");
						sb.Append (_take);
					}
					
					return sb.ToString ();
				}
			}
			
			List<Parameter> _params = new List<Parameter> ();
			List<string> _wheres = new List<string> ();
			List<OrderBy> _orderbys = new List<OrderBy> ();
			string _take;
			string _select = "*";
			
			public Collection<U> Clone<U> (Expression accExpression)
			{
				var coll = new Collection<U> (accExpression, _repo);
				coll._params.AddRange (_params);
				coll._wheres.AddRange (_wheres);
				coll._orderbys.AddRange (_orderbys);
				coll._take = _take;
				coll._select = _select;
				return coll;
			}
			
			public void ApplyWhere (Expression expression)
			{
				_wheres.Add (CompileExpression (expression));
			}
			
			public void ApplyOrderByDescending (Expression expression)
			{
				_orderbys.Add (new OrderBy {
					Text = CompileExpression (expression),
					Direction = "desc",
				});
			}
			
			public void ApplyTake (Expression expression)
			{
				_take = CompileExpression (expression);
			}
			
			string CompileParam (object value)
			{
				var index = _params.Count;
				var p = new Parameter {
					Name = ":p" + index,
					Value = value,
				};
				_params.Add (p);
				return p.Name;
			}
			
			string CompileExpression (Expression expr)
			{
				switch (expr.NodeType)
				{
				case ExpressionType.Constant:
				{
					var conste = (ConstantExpression)expr;
					if (expr.Type == typeof(string)) {
						return CompileParam (conste.Value);
					}
					else {
						return string.Format (System.Globalization.CultureInfo.InvariantCulture, "{0}", conste.Value);
					}
				}
				case ExpressionType.Equal:
				{
					var bine = (BinaryExpression)expr;
					return "(" + CompileExpression (bine.Left) + "=" + CompileExpression (bine.Right) + ")";
				}
				case ExpressionType.MemberAccess:
				{
					var meme = (MemberExpression)expr;
					var obj = meme.Expression;
					
					if (obj.NodeType == ExpressionType.Parameter) {
						return "\"" + meme.Member.Name + "\"";
					}
					else {
						return CompileParam (Eval (expr));
					}
				}
				default:
					throw new NotSupportedException ();
				}
			}
			
			object Eval (Expression expression)
			{
				return Expression.Lambda<Func<object>>(expression).Compile () ();
			}
		}
		
		class CollectionQueryProvider<U> : IQueryProvider
		{
			Collection<U> _collection;
			
			public CollectionQueryProvider (Collection<U> collection)
			{
				_collection = collection;
			}
			
			public IQueryable CreateQuery (Expression expression)
			{
				throw new NotImplementedException ();
			}
			
			public IQueryable<T> CreateQuery<T> (Expression expression)
			{
				if (expression.NodeType == ExpressionType.Call) {
					var call = (MethodCallExpression)expression;
					var newColl = _collection.Clone<T> (expression);
					switch (call.Method.Name) {
					case "Where":
						newColl.ApplyWhere (((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand).Body);
						break;
					case "OrderByDescending":
						newColl.ApplyOrderByDescending (((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand).Body);
						break;
					case "Take":
						newColl.ApplyTake (call.Arguments [1]);
						break;
					default:
						throw new NotSupportedException (call.Method.Name);
					}
					return newColl;
				}
				else {
					throw new NotSupportedException ();
				}
			}
			
			public object Execute (Expression expression)
			{
				throw new NotImplementedException ();
			}
			
			public T Execute<T> (Expression expression)
			{
				throw new NotImplementedException ();
			}
		}
		
		public override Task<QueryResult> Query (Query query)
		{
			return Task.Factory.StartNew (delegate {
				lock (_connectionLock) {
					var e = query.Perform (_collections);
					return new QueryResult (e);
				}
			});
		}
	}
}

