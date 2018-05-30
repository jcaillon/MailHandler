#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (NewMailEventArgs.cs) is part of MailHandler.
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
using System.Runtime.CompilerServices;
using MailHandler.Logger;
using MailKit;

namespace MailHandler.Events {
    
    public class BatchEventArgs : EventArgs {

        private ITracer _tracer;
        
        /// <summary>
        /// Contains action that can be applied to this received mail
        /// </summary>
        public IMailSimpleSmtpActuator SimpleSmtpActuator { get; }

        public BatchEventArgs(IMailSimpleSmtpActuator simpleSmtpActuator, ITracer tracer) {
            SimpleSmtpActuator = simpleSmtpActuator;
            _tracer = tracer;
        }

        /// <summary>
        /// Log/trace a new message in the ITracer if it exists
        /// </summary>
        /// <param name="message"></param>
        /// <param name="calledFrom"></param>
        public void Trace(string message, [CallerMemberName] string calledFrom = null) {
            _tracer.TraceUserMessage(message, calledFrom);
        }
    }
}