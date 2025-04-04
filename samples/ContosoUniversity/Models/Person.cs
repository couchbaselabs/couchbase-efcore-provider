using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace ContosoUniversity.Models
{
    public abstract class Person
    {
        [JsonProperty("iD")]
        [JsonPropertyName("iD")]
        public int ID { get; set; } 

        [Required]
        [StringLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }
        [Required]
        [StringLength(50, ErrorMessage = "First name cannot be longer than 50 characters.")]
        [JsonProperty("firstName")]
        [JsonPropertyName("firstName")]
        [Display(Name = "First Name")]
        [Column("firstName")]
        public string FirstMidName { get; set; }

        [Display(Name = "Full Name")]
        public string FullName => LastName + ", " + FirstMidName;

        public string Discriminator { get; set; }
    }
}