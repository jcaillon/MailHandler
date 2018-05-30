#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (IMailActuator.cs) is part of MailHandler.
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
using MailKit;
using MimeKit;

namespace MailHandler {

    public interface IMailSimpleSmtpActuator {
        
        /// <summary>
        /// Sends the given message
        /// </summary>
        /// <param name="message"></param>
        void SendMail(MimeMessage message);
    }
    
    public interface IMailActuator : IMailSimpleSmtpActuator{
        
        /// <summary>
        /// Downloads the entire mail from the imap server as a MimeMessage
        /// </summary>
        /// <param name="messageSummary"></param>
        /// <returns></returns>
        MimeMessage DownloadMimeMessage(IMessageSummary messageSummary);

        /// <summary>
        /// Download the given message from the imap server and then forward it
        /// </summary>
        /// <param name="messageSummary"></param>
        /// <param name="toMailAddresses"></param>
        void DownloadThenForwardMessage(IMessageSummary messageSummary, List<string> toMailAddresses);

        /// <summary>
        /// Forwards the given message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="toMailAddresses"></param>
        void ForwardMessage(MimeMessage message, List<string> toMailAddresses);

        /// <summary>
        /// Download the message body from the imap server
        /// </summary>
        /// <param name="messageSummary"></param>
        /// <returns></returns>
        string DownloadMessageBody(IMessageSummary messageSummary);

        /// <summary>
        /// Downloads and saves all the attachment for the given message in the given directory
        /// </summary>
        /// <param name="message"></param>
        /// <param name="directory"></param>
        /// <param name="onException"></param>
        /// <returns>List of saved files</returns>
        List<string> DownloadThenSaveAttachments(IMessageSummary message, string directory, Action<Exception> onException = null);

        /// <summary>
        /// Do an action with each attachment for the given message
        /// </summary>
        /// <param name="mail"></param>
        /// <param name="attachmentHandler"></param>
        /// <param name="onException"></param>
        /// <returns>List of saved files</returns>
        void DoForEachAttachments(MimeMessage mail, Action<string, byte[]> attachmentHandler, Action<Exception> onException = null);

        /// <summary>
        /// Downloads and do an action with each attachment for the given message
        /// </summary>
        /// <param name="mail"></param>
        /// <param name="attachmentHandler"></param>
        /// <param name="onException"></param>
        /// <returns>List of saved files</returns>
        void DownloadThenDoForEachAttachments(IMessageSummary mail, Action<string, byte[]> attachmentHandler, Action<Exception> onException = null);

        /// <summary>
        /// Saves all the attachments of the given message in the given directory
        /// </summary>
        /// <param name="message"></param>
        /// <param name="directory"></param>
        /// <param name="onException"></param>
        /// <returns>List of saved files</returns>
        List<string> SaveAttachments(MimeMessage message, string directory, Action<Exception> onException = null);
    }
}