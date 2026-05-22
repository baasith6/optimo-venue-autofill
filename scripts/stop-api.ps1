# Stop any running VenueAutofill.Api instance (fixes "file is locked" build errors)
Get-Process -Name "VenueAutofill.Api" -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "Stopped VenueAutofill.Api (if it was running)."
