using System;
using System.Collections.Generic;
using System.Linq;

using MonoTouch.Foundation;
using MonoTouch.UIKit;
using Rouse;
using System.Threading.Tasks;

namespace Chat
{
	[Register ("AppDelegate")]
	public partial class AppDelegate : UIApplicationDelegate
	{
		UIWindow window;

		public override bool FinishedLaunching (UIApplication app, NSDictionary options)
		{
			window = new UIWindow (UIScreen.MainScreen.Bounds);
			
			var repo = new CacheRepository (new HttpRepository ("127.0.0.1"));
			
			var channel = new ChannelViewController ("monotouch", repo);
			
			Stuff (new Fact ());
			
			window.RootViewController = new UINavigationController (channel);
			
			window.MakeKeyAndVisible ();
			
			return true;
		}
		
		void Stuff (IFactory f)
		{
			Console.WriteLine (f.Get <Foo> ());
		}
	}
	
	class Foo
	{
		public override string ToString ()
		{
			return string.Format ("[Foo]");
		}
	}
	
	interface IFactory
	{
		T Get<T> ();
	}
	
	class Fact : IFactory
	{
		public T Get<T> ()
		{
			return (T)Activator.CreateInstance (typeof(T));
		}
	}
	
	class ChannelViewController : UITableViewController
	{
		UIAlertView _alert;
		
		public ChannelViewController (string channel, Repository repo)
		{
			Title = channel;
			
			var data = new Data ();
			var query = new RecentChannelMessages (channel);
			
			repo.Query (query).ContinueWith ((task) => {
				if (task.Exception != null) {
					var ex = (Exception)task.Exception;
					while (ex.InnerException != null) {
						ex = ex.InnerException;
					}
					_alert = new UIAlertView (ex.GetType ().Name, ex.Message, null, "OK");
					_alert.Show ();
				}
				else {
					data.Messages = task.Result;
					TableView.ReloadData ();
				}				
			}, new DispatchQueueScheduler ());
		}
		
		class Data : UITableViewDataSource
		{
			public QueryResult<ChannelMessage> Messages;
			
			public override int NumberOfSections (UITableView tableView)
			{
				return 1;
			}
			
			public override int RowsInSection (UITableView tableView, int section)
			{
				return Messages != null ? Messages.Count : 0;
			}
			
			public override UITableViewCell GetCell (UITableView tableView, NSIndexPath indexPath)
			{
				var cell = tableView.DequeueReusableCell ("C");
				if (cell == null) {
					cell = new UITableViewCell (UITableViewCellStyle.Default, "C");
				}
				
				var message = Messages [indexPath.Row];
				
				cell.TextLabel.Text = message.Username + ": " + message.Text;
				
				return cell;
			}
		}
	}
}

