using System;
using System.Collections.Generic;
using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Services
{
    // One photo's worth of data assembled by PhotoExportService, ready to hand to either format
    // builder — ImageBytes is already annotated with damage-mark pins (or not) by that point, so
    // neither builder needs to know includeDamageMarks was ever a choice.
    public record PhotoExportItem(
        string FileNameBase,
        byte[] ImageBytes,
        Stage Stage,
        Part? Part,
        DateTime UploadedAtUtc,
        IReadOnlyList<PhotoExportComment> Comments);

    public record PhotoExportComment(string AuthorEmail, DateTime CreatedAtUtc, string Body);
}
