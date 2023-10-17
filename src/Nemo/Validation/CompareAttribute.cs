﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;

namespace Nemo.Validation;

public enum CompareOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class CompareAttribute : ValidationAttributeBase
{
    private const string DEFAULT_ERROR_MESSAGE = "The field {{0}} is {0} {1}.";
    
    public CompareAttribute(CompareOperator compareOperator, string propertyName)
        : base()
    {
        this.Operator = compareOperator;
        this.PropertyName = propertyName;
        this.InitializeDefaultErrorMessage();
    }

    public string PropertyName { get; set; }

    public CompareOperator Operator { get; set; }

    public Type ComparerType { get; set; }

    protected override void InitializeDefaultErrorMessage()
    {
        if (string.IsNullOrEmpty(this.DefaultErrorMessage))
        {
            string operatorValue = null;
            if (this.Operator == CompareOperator.NotEqual)
            {
                operatorValue = "equal to";
            }
            else
            {
                operatorValue = "not " + string.Join(" ", Regex.Split(this.Operator.ToString(), "(?=[A-Z])").ToArray()).ToLower();
                if (this.Operator != CompareOperator.GreaterThan && this.Operator != CompareOperator.LessThan)
                {
                    operatorValue += " to";
                }
            }
            this.DefaultErrorMessage = string.Format(DEFAULT_ERROR_MESSAGE, operatorValue, this.PropertyName);
        }
    }

    public override bool IsValid(object value)
    {
        if (value != null && value.GetType().IsArray)
        {
            object[] values = (object[])value;
            if (values.Length > 1)
            {
                object v1 = values[0];
                object v2 = values[1];

                if (v1 != null && v2 != null && v1.GetType() == v2.GetType())
                {
                    IComparer comparer = null;
                    if (ComparerType != null && typeof(IComparer).IsAssignableFrom(ComparerType))
                    {
                        comparer = (IComparer)Reflection.Activator.New(ComparerType);
                    }
                    else
                    {
                        comparer = Comparer<IComparable>.Default;
                    }

                    switch (this.Operator)
                    {
                        case CompareOperator.Equal:
                            return object.Equals(v1, v2);
                        case CompareOperator.NotEqual:
                            return !object.Equals(v1, v2);
                        case CompareOperator.GreaterThan:
                            if (ComparerType != null)
                            {
                                return comparer.Compare(v1, v2) > 0;
                            }
                            else if (v1 is IComparable && v2 is IComparable)
                            {
                                return comparer.Compare((IComparable)v1, (IComparable)v2) > 0;
                            }
                            return false;
                        case CompareOperator.GreaterThanOrEqual:
                            if (ComparerType != null)
                            {
                                return comparer.Compare(v1, v2) >= 0;
                            }
                            else if (v1 is IComparable && v2 is IComparable)
                            {
                                return comparer.Compare((IComparable)v1, (IComparable)v2) >= 0;
                            }
                            return false;
                        case CompareOperator.LessThan:
                            if (ComparerType != null)
                            {
                                return comparer.Compare(v1, v2) < 0;
                            }
                            else if (v1 is IComparable && v2 is IComparable)
                            {
                                return comparer.Compare((IComparable)v1, (IComparable)v2) < 0;
                            }
                            return false;
                        case CompareOperator.LessThanOrEqual:
                            if (ComparerType != null)
                            {
                                return comparer.Compare(v1, v2) <= 0;
                            }
                            else if (v1 is IComparable && v2 is IComparable)
                            {
                                return comparer.Compare((IComparable)v1, (IComparable)v2) <= 0;
                            }
                            return false;
                    }
                }

            }
        }
        return true;
    }

    public bool IsValid<T>(T value1, T value2)
    {
        return IsValid(new object[] { value1, value2 });
    }
}
