﻿// <auto-generated />
using System;
using MdcAi.ChatUI.LocalDal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    [DbContext(typeof(UserProfileDbContext))]
    [Migration("20231206123521_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.14");

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbCategory", b =>
                {
                    b.Property<string>("IdCategory")
                        .HasColumnType("TEXT");

                    b.Property<string>("Description")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<string>("SystemMessage")
                        .HasColumnType("TEXT");

                    b.HasKey("IdCategory");

                    b.ToTable("Categories");

                    b.HasData(
                        new
                        {
                            IdCategory = "default",
                            Description = "General Purpose AI Assistant",
                            Name = "General",
                            SystemMessage = "You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks."
                        });
                });

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbConversation", b =>
                {
                    b.Property<string>("IdConversation")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedTs")
                        .HasColumnType("TEXT");

                    b.Property<string>("IdCategory")
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsTrash")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.HasKey("IdConversation");

                    b.HasIndex("IdCategory");

                    b.ToTable("Conversations");
                });

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbMessage", b =>
                {
                    b.Property<string>("IdMessage")
                        .HasColumnType("TEXT");

                    b.Property<string>("Content")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("CreatedTs")
                        .HasColumnType("TEXT");

                    b.Property<string>("IdConversation")
                        .HasColumnType("TEXT");

                    b.Property<string>("IdMessageParent")
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsCurrentVersion")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsTrash")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Role")
                        .HasColumnType("TEXT");

                    b.Property<int>("Version")
                        .HasColumnType("INTEGER");

                    b.HasKey("IdMessage");

                    b.HasIndex("IdConversation");

                    b.ToTable("Messages");
                });

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbConversation", b =>
                {
                    b.HasOne("MdcAi.ChatUI.LocalDal.DbCategory", "Category")
                        .WithMany()
                        .HasForeignKey("IdCategory");

                    b.Navigation("Category");
                });

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbMessage", b =>
                {
                    b.HasOne("MdcAi.ChatUI.LocalDal.DbConversation", "Conversation")
                        .WithMany("Messages")
                        .HasForeignKey("IdConversation");

                    b.Navigation("Conversation");
                });

            modelBuilder.Entity("MdcAi.ChatUI.LocalDal.DbConversation", b =>
                {
                    b.Navigation("Messages");
                });
#pragma warning restore 612, 618
        }
    }
}
