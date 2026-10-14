using Scriban;
using Scriban.Runtime;
using Stellaegis.Primitives;

namespace Stellaegis.Messaging.Templates;

/// <inheritdoc cref="ITemplateEmailSender"/>
internal sealed class TemplateEmailSender(
    ISystemTemplateProvider templateProvider,
    IEmailSender emailSender) : ITemplateEmailSender
{
    public async Task SendAsync(
        Space space,
        string templateName,
        object data,
        string to,
        string subject,
        string? toName = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await templateProvider.GetTemplateAsync(templateName, cancellationToken);
        var html = await RenderAsync(raw, data);

        await emailSender.SendAsync(space, new EmailMessage
        {
            To = [new EmailAddress(to, toName)],
            Subject = subject,
            HtmlBody = html
        }, cancellationToken);
    }

    private static async Task<string> RenderAsync(string template, object data)
    {
        var tpl = Template.Parse(template);
        if (tpl.HasErrors)
            throw new InvalidOperationException(
                $"Template parse error(s): {string.Join("; ", tpl.Messages)}");

        var context = new TemplateContext { StrictVariables = false };
        context.PushGlobal(ToScriptObject(data));
        return await tpl.RenderAsync(context);
    }

    private static ScriptObject ToScriptObject(object data)
    {
        var obj = new ScriptObject();
        foreach (var prop in data.GetType().GetProperties())
        {
            var key = ToSnakeCase(prop.Name);
            obj[key] = prop.GetValue(data);
        }
        return obj;
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
