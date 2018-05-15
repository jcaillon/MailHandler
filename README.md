# MailHandler

## About this project

A simple classlibrary that connect to a mail server with imap+smtp and allows to react to new mail arriving in the inbox.

This can serve as a fundation for a mail bot. It provides minimal features but can be easily extended.

## Notes

If new mails arrive while the `NewMessage` is being executed, they are simply put into a queue and they will be executed on the same thread as the current `NewMessage` event handler when it is done.

This mean this you will be able to handle all the mails, even if you block the thread when handling new messages.

*Remarks* : for the smtp client, we try to keep the connection open by sending noOp commands every x minutes. However, it doesn't seem to work for all servers. This is ok because if an smtp command fails because of a disconnection, we will automatically reconnect and play the command again.

## Usage

You will find a usage example below :

```C#
using (var inboxHandler = new InboxMailHandler(config)) {
    inboxHandler.NewMessage += InboxHandlerOnNewMessage;
    TraceLogger.Instance.TraceUserMessage(">>> listening...");
    inboxHandler.StartListening();
    TraceLogger.Instance.TraceUserMessage(">>> waiting for input to exit...");
    Console.ReadKey();
    inboxHandler.StopListening();
}
```


```C#
private static void InboxHandlerOnNewMessage(object sender, NewMailEventArgs e) {
    e.Trace($"SUBJECT => {e.MessageSummary.Envelope.Subject}");
    e.Trace($"FROM => {e.MessageSummary.Envelope.From.Format()}");
    e.Trace($"TO => {e.MessageSummary.Envelope.To.Format()}");
    e.Trace($"CC => {e.MessageSummary.Envelope.Cc.Format()}");
    e.Trace($"DATE => {e.MessageSummary.Envelope.Date}");

    e.Trace($"BODY => {e.MailActuator.DownloadMessageBody(e.MessageSummary)}");

    var savedFiles = e.MailActuator.DownloadAndSaveAttachments(e.MessageSummary, Path.Combine(AppContext.BaseDirectory, "temp"));
    if (savedFiles != null) {
        foreach (var savedFile in savedFiles) {
            TraceLogger.Instance.TraceInformation($"ATTACHMENT DOWNLOADED AND SAVED IN : {savedFile}");
        }
    }

    e.Trace($"FORWARDING...");
    e.MailActuator.DownloadThenForwardMessage(e.MessageSummary, new List<string>() {
        "julien.caillon@random.com"
    });

    e.Trace($"DOWNLOADING MESSAGE FROM IMAP...");
    var mime = e.MailActuator.DownloadMimeMessage(e.MessageSummary);

    savedFiles = e.MailActuator.SaveAttachments(mime, Path.Combine(AppContext.BaseDirectory, "temp2"));
    if (savedFiles != null) {
        foreach (var savedFile in savedFiles) {
            TraceLogger.Instance.TraceInformation($"ATTACHMENT SAVED IN : {savedFile}");
        }
    }

    e.Trace($"FORWARDING...");
    e.MailActuator.ForwardMessage(mime, new List<string>() {
        "julien.caillon@random.com"
    });

    e.Trace($"NEW MESSAGE...");
    var newMessage = new MimeMessage {
        Subject = "Un nouveau mail",
        Body = new BodyBuilder {
            TextBody = "coucou"
        }.ToMessageBody()
    };
    newMessage.To.Add(new MailboxAddress("Julien", "julien.caillon@random.com"));
    e.MailActuator.SendMail(newMessage);

    e.MoveToSubfolder = "_treated";
    //e.ToDelete = true;
}
```