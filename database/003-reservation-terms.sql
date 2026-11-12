-- Additive, idempotent. Existing bookings retain unknown historical terms (no backfill).
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.HotelCancellationTerms',N'U') IS NULL
  CREATE TABLE dbo.HotelCancellationTerms(
   QuoteId uniqueidentifier NOT NULL PRIMARY KEY REFERENCES dbo.HotelQuotes(QuoteId),
   BId int NULL REFERENCES dbo.Booking(BId) ON DELETE CASCADE,
   PolicyUri nvarchar(200) NOT NULL, PolicyVersion nvarchar(30) NOT NULL,
   HotelTimeZone nvarchar(80) NOT NULL, CheckInAt datetimeoffset NOT NULL,
   FreeCancellationUntil datetimeoffset NOT NULL,
   LateCancellationFee decimal(18,2) NOT NULL CHECK(LateCancellationFee>=0));
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.HotelCancellationTerms') AND name=N'UX_HotelCancellationTerms_BId')
  CREATE UNIQUE INDEX UX_HotelCancellationTerms_BId ON dbo.HotelCancellationTerms(BId) WHERE BId IS NOT NULL;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
