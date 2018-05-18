#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (AsapButDelayableAction.cs) is part of MailHandler.
// 
// MailHandler is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// MailHandler is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MailHandler. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

namespace MailHandler.Utilities {
    internal class AsapButDelayableAction : IDisposable {
        
        #region Static

        private static List<AsapButDelayableAction> _savedActions = new List<AsapButDelayableAction>();

        /// <summary>
        /// Clean all delayed actions started
        /// </summary>
        public static void CleanAll() {
            foreach (var action in _savedActions.ToList().Where(action => action != null)) {
                action.Dispose();
            }
        }

        #endregion

        #region private

        private object _lock = new object();
        private Timer _timer;
        private Action _toDo;
        private DateTime _timerInitialisationDateTime = DateTime.Now;
        private int _msDelay;
        private int _msMaxDelay;

        #endregion

        #region Constructor

        public AsapButDelayableAction(int msDelay, int msMaxDelay, Action toDo) {
            _msDelay = msDelay;
            _msMaxDelay = msMaxDelay;
            _toDo = toDo;
            _savedActions.Add(this);
        }

        #endregion

        #region Public

        /// <summary>
        /// Start the action with a delay, delay that can be extended if this method is called again within the
        /// delay
        /// </summary>
        public void DoDelayable() {
            // do on delay, can be delayed event more if this method is called again
            if (_msDelay > 0) {
                lock (_lock) {
                    if (_timer == null) {
                        // init timer
                        _timer = new Timer(_msDelay) {
                            AutoReset = false
                        };
                        _timer.Elapsed += TimerTick;
                        _timer.Start();
                        _timerInitialisationDateTime = DateTime.Now;
                        return;
                    }

                    if (DateTime.Now.Subtract(_timerInitialisationDateTime).TotalMilliseconds < _msMaxDelay) {
                        // reset timer
                        _timer.Stop();
                        _timer.Start();
                        return;
                    }
                }
            }

            Task.Factory.StartNew(DoTaskNow);
        }

        /// <summary>
        /// Forces to do the action now
        /// </summary>
        public void DoTaskNow() {
            TimerTick(this, null);
        }

        #endregion

        #region Private

        private void TimerTick(object sender, ElapsedEventArgs elapsedEventArgs) {
            Cancel();
            _toDo();
        }

        #endregion

        #region Stop

        /// <summary>
        /// Stop the recurrent action
        /// </summary>
        public void Cancel() {
            lock (_lock) {
                _timer?.Stop();
                _timer?.Close();
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() {
            try {
                Cancel();
            } finally {
                _savedActions.Remove(this);
            }
        }

        #endregion
    }
}