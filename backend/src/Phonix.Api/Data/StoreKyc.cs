using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly List<KycRequest> _kyc = new();
    private int _kycSeq;

    public IReadOnlyList<KycRequest> GetAllKyc(KycStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<KycRequest> q = _kyc;
            if (status is KycStatus s) q = q.Where(k => k.Status == s);
            return q.OrderByDescending(k => k.Id).ToList();
        }
    }

    public KycRequest? GetKycForUser(int userId)
    {
        lock (_gate) return _kyc.Where(k => k.UserId == userId).OrderByDescending(k => k.Id).FirstOrDefault();
    }

    public KycRequest SubmitKyc(KycRequest input)
    {
        lock (_gate)
        {
            var existing = _kyc.FirstOrDefault(k => k.UserId == input.UserId);
            if (existing is null)
            {
                input.Id = ++_kycSeq;
                input.Status = KycStatus.Pending;
                input.Note = null;
                if (string.IsNullOrWhiteSpace(input.Date)) input.Date = Today();
                _kyc.Add(input);
                return input;
            }

            existing.FullName = input.FullName;
            existing.NationalId = input.NationalId;
            existing.BirthDate = input.BirthDate;
            existing.CardImage = input.CardImage;
            existing.SelfieImage = input.SelfieImage;
            existing.Status = KycStatus.Pending;
            existing.Note = null;
            existing.Date = Today();
            return existing;
        }
    }

    public KycRequest? SetKycStatus(int id, KycStatus status, string? note)
    {
        lock (_gate)
        {
            var req = _kyc.FirstOrDefault(k => k.Id == id);
            if (req is null) return null;
            req.Status = status;
            req.Note = note;
            // surface an explicit rejection reason to the user; clear it when not a rejection.
            req.RejectionReason = status == KycStatus.Rejected ? note : null;

            // approving the national-ID KYC lifts the user to level 2 (full access, permanent). A rejection
            // never lowers an already-granted level.
            var user = _users.FirstOrDefault(u => u.Id == req.UserId);
            if (user is not null && status == KycStatus.Approved)
            {
                user.VerificationLevel = 2;
                user.Verified = true;
            }
            return req;
        }
    }

    private void SeedKyc()
    {
        _kyc.Add(new KycRequest
        {
            Id = ++_kycSeq,
            UserId = 6,
            FullName = "نگار شریفی",
            NationalId = "۰۰۱۲۳۴۵۶۷۸",
            BirthDate = "۱۳۷۵/۰۵/۱۲",
            CardImage = "/figma/prod-freelancer.png",
            SelfieImage = "",
            Status = KycStatus.Pending,
            Date = "۱۴۰۳/۰۳/۲۰",
        });
    }
}
