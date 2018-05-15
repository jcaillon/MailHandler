#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (ClientFactory.cs) is part of MailHandler.
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
using MailKit.Net.Imap;
using MailKit.Net.Smtp;

namespace MailHandler {
    internal class ClientFactory : IClientFactory {
        private readonly IMailHandlerConfiguration _configuration;

        public ClientFactory(IMailHandlerConfiguration config) {
            _configuration = config;
        }

        public virtual ImapClient GetImapClient() {
            var imapClient = new ImapClient(_configuration.ImapProtocolLogger);
            imapClient.Connect(_configuration.ImapHostName, _configuration.ImapPort, _configuration.ImapUseSsl);
            imapClient.Authenticate(_configuration.ImapUserName, _configuration.ImapPassword);
            return imapClient;
        }

        public virtual SmtpClient GetSmtpClient() {
            var client = new SmtpClient(_configuration.SmtpProtocolLogger);
            client.Connect(_configuration.SmtpHostName, _configuration.SmtpPort, _configuration.StmpSecureOption);
            //client.AuthenticationMechanisms.Remove("XOAUTH2");
            client.Authenticate(_configuration.SmtpUserName, _configuration.SmtpPassword);
            return client;
        }
    }
}