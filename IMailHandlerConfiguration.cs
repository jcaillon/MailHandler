#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (IMailHandlerConfiguration.cs) is part of MailHandler.
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
using MailHandler.Logger;
using MailKit;
using MailKit.Security;

namespace MailHandler {
    public interface IMailHandlerConfiguration {
        string MailBoxId { get; }
        string ImapUserName { get; }
        string ImapPassword { get; }
        string ImapHostName { get; }
        int ImapPort { get; }
        bool ImapUseSsl { get; }
        string SmtpUserName { get; }
        string SmtpPassword { get; }
        string SmtpHostName { get; }
        int SmtpPort { get; }
        SecureSocketOptions StmpSecureOption { get; }
        string SenderMailAddress { get; }
        string SenderName { get; }

        ITracer Tracer { get; }
        IProtocolLogger ImapProtocolLogger { get; }
        IProtocolLogger SmtpProtocolLogger { get; }

        /// <summary>
        /// Set to null to use the default factory
        /// </summary>
        IClientFactory ClientFactory { get; }
        
        /// <summary>
        /// The minimum delay (in milliseconds) that we should wait before handling a new arriving mail,
        /// this allows to treat several mails as a "batch", 
        /// </summary>
        int MinimumDelayBeforeHandlingNewMails { get; }
        
        /// <summary>
        /// 
        /// </summary>
        int MaximumDelayBeforeHandlingNewMails { get; }
    }
}