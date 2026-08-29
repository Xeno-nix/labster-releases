using System.ComponentModel.DataAnnotations;

namespace CLMS.Application.Logging.Dtos;

/// <summary>Mirrors Laravel's GuestTimeInRequest.</summary>
public class GuestTimeInRequest
{
    [Required(ErrorMessage = "Please enter the name on the reservation.")]
    [StringLength(150)]
    [Display(Name = "Reservation Booked Under")]
    public string BookedUnder { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter your full name.")]
    [StringLength(150)]
    [Display(Name = "Your Full Name")]
    public string GuestName { get; set; } = string.Empty;
}
