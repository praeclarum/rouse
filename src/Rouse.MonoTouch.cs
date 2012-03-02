using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rouse.UIKit
{
}

namespace System.Threading.Tasks
{
	public class DispatchQueueScheduler : TaskScheduler
	{
		class ScheduledTask
		{
			public Task TheTask;
			public bool ShouldRun = true;
			public bool IsRunning = false;
		}

		object _taskListLock = new object ();
		List<ScheduledTask> _taskList = new List<ScheduledTask> ();

		MonoTouch.CoreFoundation.DispatchQueue _queue;
		MonoTouch.Foundation.NSRunLoop _runLoop;
		
		public DispatchQueueScheduler ()
		{
			_queue = MonoTouch.CoreFoundation.DispatchQueue.MainQueue;
			_runLoop = MonoTouch.Foundation.NSRunLoop.Main;
		}

		public DispatchQueueScheduler (MonoTouch.CoreFoundation.DispatchQueue queue)
		{
			if (queue == null)
				throw new ArgumentNullException ("queue");
			_queue = queue;
		}

		public override int MaximumConcurrencyLevel {
			get {
				return int.MaxValue;
			}
		}

		protected override IEnumerable<Task> GetScheduledTasks ()
		{
			lock (_taskListLock) {
				return _taskList.Select (x => x.TheTask).ToList ();
			}
		}

		protected override void QueueTask (Task task)
		{
			if (task == null)
				throw new ArgumentNullException ("task");

			var t = new ScheduledTask () { TheTask = task };

			lock (_taskListLock) {
				//
				// Cleanout the task list before adding this new task
				//
				_taskList = _taskList.Where (x => x.ShouldRun && !x.IsRunning).ToList ();
				_taskList.Add (t);
			}
			
			_runLoop.BeginInvokeOnMainThread (delegate {
			//_queue.DispatchAsync (delegate {
				if (t.ShouldRun) {
					t.IsRunning = true;
					base.TryExecuteTask (t.TheTask);
				}
			});
		}

		protected override bool TryDequeue (Task task)
		{
			var t = default (ScheduledTask);

			lock (_taskListLock) {
				t = _taskList.FirstOrDefault (x => x.TheTask == task);
			}

			if (t != null && !t.IsRunning) {
				t.ShouldRun = false;
				return !t.IsRunning;
			} else {
				return false;
			}
		}

		protected override bool TryExecuteTaskInline (Task task, bool taskWasPreviouslyQueued)
		{
			if (task == null)
				throw new ArgumentNullException ("task");

			//
			// Are we in the right NSRunLoop?
			//
			var curQueue = MonoTouch.CoreFoundation.DispatchQueue.CurrentQueue;

			if ((curQueue != null) && (curQueue.Handle == _queue.Handle)) {

				//
				// Our dequeue is really simple, so just say no if this
				// task was queued before
				//
				if (taskWasPreviouslyQueued)
					return false;

				//
				// Run it on this thread
				//
				return base.TryExecuteTask (task);

			} else {
				return false;
			}
		}
	}
}

