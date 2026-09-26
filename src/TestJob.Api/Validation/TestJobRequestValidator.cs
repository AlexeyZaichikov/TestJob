using FluentValidation;
using TestJob.Api.Models;

namespace TestJob.Api.Validation;

public sealed class TestJobRequestValidator : AbstractValidator<TestJobRequest>
{
    public TestJobRequestValidator()
    {
        RuleFor(x => x.Selector)
            .NotNull().WithMessage("'selector' is required.")
            .NotEmpty().WithMessage("'selector' must not be empty.");

        RuleFor(x => x.Attribute)
            .NotNull().WithMessage("'attribute' is required.")
            .NotEmpty().WithMessage("'attribute' must not be empty.");

        RuleFor(x => x.UrlB64)
            .NotNull().WithMessage("'url_b64' is required.")
            .NotEmpty().WithMessage("'url_b64' must not be empty.");

        RuleFor(x => x.EncryptedTextBytesB64)
            .NotNull().WithMessage("'encrypted_text_bytes_b64' is required.")
            .NotEmpty().WithMessage("'encrypted_text_bytes_b64' must not be empty.");

        RuleFor(x => x.KeyBytesB64)
            .NotNull().WithMessage("'key_bytes_b64' is required.")
            .NotEmpty().WithMessage("'key_bytes_b64' must not be empty.");

        RuleFor(x => x.PageB64)
            .NotNull().WithMessage("'page_b64' is required.")
            .NotEmpty().WithMessage("'page_b64' must not be empty.");
    }
}