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
			
			window.RootViewController = new UINavigationController (channel);
			
			window.MakeKeyAndVisible ();
			
			return true;
		}
		
		static UIAlertView _alert;
		public static void ShowError (string context, Exception ex)
		{
			while (ex.InnerException != null) {
				ex = ex.InnerException;
			}
			Console.WriteLine (ex);
			_alert = new UIAlertView (context + " " + ex.GetType ().Name, ex.Message, null, "OK");
			_alert.Show ();
		}
	}

	class ChannelViewController : UITableViewController
	{
		Repository _repo;
		string _channel;
		
		public ChannelViewController (string channel, Repository repo)
		{
			_channel = channel;
			_repo = repo;
			
			Title = channel;			
			TableView.DataSource = new Data ();
			
			NavigationItem.RightBarButtonItem = new UIBarButtonItem (
				UIBarButtonSystemItem.Compose,
				delegate {
					var c = new NewMessageViewController (channel, repo);
					PresentModalViewController (new UINavigationController (c), true);
			});
			
			Refresh ();
		}
		
		void Refresh ()
		{
			var query = new RecentChannelMessages (_channel);
			
			_repo.Get<Message> (query).ContinueWith ((task) => {
				if (task.IsFaulted) {
					AppDelegate.ShowError ("Refresh", task.Exception);
				}
				else {
					((Data)TableView.DataSource).Messages = task.Result;
					TableView.ReloadData ();
				}				
			}, new DispatchQueueScheduler ());
		}
		
		class Data : UITableViewDataSource
		{
			public QueryResult<Message> Messages;
			
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
	
	class NewMessageViewController : UIViewController
	{
		UITextView _text;
		
		public NewMessageViewController (string channel, Repository repo)
		{
			Title = "New Message";
			
			_text = new UITextView (View.Bounds) {
				Font = UIFont.SystemFontOfSize (24),
			};
			View.AddSubview (_text);
			
			NavigationItem.LeftBarButtonItem = new UIBarButtonItem (
				UIBarButtonSystemItem.Cancel,
				delegate {
					DismissModalViewControllerAnimated (true);
			});
			
			NavigationItem.RightBarButtonItem = new UIBarButtonItem (
				"Send",
				UIBarButtonItemStyle.Done,
				delegate {
					NavigationItem.RightBarButtonItem.Enabled = false;
					var message = new Message {
						ChannelName = channel,
						PostTime = DateTime.UtcNow,
						Username = "fak",
						Text = _text.Text,
					};
					repo.Save (message).ContinueWith ((task) => {
						if (task.IsFaulted) AppDelegate.ShowError ("Send", task.Exception);
						DismissModalViewControllerAnimated (true);
					}, new DispatchQueueScheduler ());
				});
		}
		
		public override void ViewWillAppear (bool animated)
		{
			base.ViewWillAppear (animated);
			_text.BecomeFirstResponder ();
		}
	}
}

