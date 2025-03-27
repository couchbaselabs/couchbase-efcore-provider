﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace ContosoUniversity.Models
{
    public abstract class Person
    {
        public int ID { get; set; } 

        [Required]
        [StringLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }
        [Required]
        [StringLength(50, ErrorMessage = "First name cannot be longer than 50 characters.")]
        [JsonProperty("FirstName")]
        [JsonPropertyName("FirstName")]
        [Display(Name = "First Name")]
        [Column("FirstName")]
        public string FirstMidName { get; set; }

        [Display(Name = "Full Name")]
        public string FullName => LastName + ", " + FirstMidName;

        public string Discriminator { get; set; }
    }
}