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
            modelBuilder.Entity<Course>().ToCouchbaseCollection(this, "course");
            modelBuilder.Entity<Enrollment>().ToCouchbaseCollection(this, "enrollment");
            modelBuilder.Entity<Student>().ToCouchbaseCollection(this, "person");
            modelBuilder.Entity<Instructor>().ToCouchbaseCollection(this, "person");
            modelBuilder.Entity<Person>().ToCouchbaseCollection(this, "person");
            modelBuilder.Entity<Department>().ToCouchbaseCollection(this, "department");
            modelBuilder.Entity<OfficeAssignment>().ToCouchbaseCollection(this, "officeAssignment");
            modelBuilder.Entity<CourseAssignment>().ToCouchbaseCollection(this, "courseAssignment");
            modelBuilder.Entity<CourseAssignment>()
                .HasKey(c => new { c.CourseID, c.InstructorID });
        }
    }
}