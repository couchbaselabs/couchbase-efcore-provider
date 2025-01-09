using System.Runtime.CompilerServices;
using ContosoUniversity.Models;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ContosoUniversity.Data
{
    public class SchoolContext : DbContext
    {
        public SchoolContext(DbContextOptions<SchoolContext> options) : base(options)
        {
        }

        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Instructor> Instructors { get; set; }
        public DbSet<OfficeAssignment> OfficeAssignments { get; set; }
        public DbSet<CourseAssignment> CourseAssignments { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Course>().ToCouchbaseCollection("Course");
            modelBuilder.Entity<Enrollment>().ToCouchbaseCollection("Enrollment");
            modelBuilder.Entity<Student>().ToCouchbaseCollection("Person");
            modelBuilder.Entity<Instructor>().ToCouchbaseCollection("Person");
            modelBuilder.Entity<Person>().ToCouchbaseCollection("Person");
            modelBuilder.Entity<Department>().ToCouchbaseCollection("Department");
            modelBuilder.Entity<OfficeAssignment>().ToCouchbaseCollection("OfficeAssignment");
            modelBuilder.Entity<CourseAssignment>().ToCouchbaseCollection("CourseAssignment");
            modelBuilder.Entity<CourseAssignment>()
                .HasKey(c => new { c.CourseID, c.InstructorID });
        }
    }
}